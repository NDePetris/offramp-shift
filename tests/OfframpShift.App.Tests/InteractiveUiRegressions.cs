using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;

internal static class InteractiveUiRegressions
{
    // Program uses a plain resource-only Application: pumping cannot start monitoring.
    private sealed class UiContinuations : IDisposable
    {
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;
        internal UiContinuations() => SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        internal void Drain()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                () => frame.Continue = false);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        public void Dispose() => SynchronizationContext.SetSynchronizationContext(previous);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    private sealed class Platform : INudgePlatform
    {
        public nint ForegroundWindow { get; set; } = 1;
        public bool Placement { get; set; } = true;
        public bool Activation { get; set; } = true;
        public string PlacementStatus => "ContentMeasurement";
        public Action<NudgeWindow>? DuringPresentation { get; set; }
        public int Shows, Activations;
        public Task<bool> PresentAsync(NudgeWindow window, nint foreground)
        { if (Placement) Shows++; DuringPresentation?.Invoke(window); return Task.FromResult(Placement); }
        public bool Activate(NudgeWindow window) { Activations++; return Activation; }
    }
    internal static void Run(Action<string, Action> test, Func<HostSnapshot> snapshot)
    {
        void Check(bool value) { if (!value) throw new InvalidOperationException("Interactive UI assertion failed."); }
        HostSnapshot Eligible()
        {
            var value = snapshot();
            return value with
            {
                CanKeepNudgeOpen = true,
                SwitchingUnavailableReason = "",
                Settings = value.Settings with { ReminderStyle = ReminderStyle.InteractiveWindow, SwitchingConsent = true, SwitchingConsentBuild = value.Settings.Build },
                State = value.State! with { Exhausted = true, Intervention = InterventionState.Nudging }
            };
        }
        ReminderEvent Event(HostSnapshot value, long sequence = 1) => new(sequence, value.ReminderGeneration, 1, value.Settings.Personal!.Value,
            Environment.TickCount64 + 2_000, ReminderStyle.InteractiveWindow, value.ReminderEpoch);
        HostCommandResult Accept(HostCommand command, ref HostSnapshot value)
        {
            if (command.Kind == HostCommandKind.BeginNudgePresentation)
            {
                var transaction = new NudgePresentation(Guid.NewGuid(), command.Reminder!.Sequence,
                    command.Reminder.ForegroundWindow, value.Settings.Personal!.Value, value.ReminderEpoch, Environment.TickCount64 + 2_000);
                value = value with { PresentationId = transaction.Id };
                return HostCommandResult.Success() with { Presentation = transaction };
            }
            return HostCommandResult.Success() with { Presentation = command.Presentation };
        }
        test("authorized reminder navigation reuses the callback without closing replaying or submitting", () =>
        {
            var value = Eligible(); var opens = 0; var requests = new List<HostCommandKind>();
            var platform = new Platform();
            var presenter = new NudgePresenter(command =>
            { requests.Add(command.Kind); return Task.FromResult(Accept(command, ref value)); }, () => value,
                _ => { }, platform, openMain: () => opens++);
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
            var target = presenter.Window!; var count = requests.Count;
            target.OpenMainButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            target.OpenMainButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            presenter.Refresh();
            Check(opens == 2 && requests.Count == count && presenter.Window == target && target.CanReportFailure);
            Check(platform.Shows == 1 && platform.Activations == 1);
            Check(!requests.Contains(HostCommandKind.DeferInteractive));
            presenter.Close();
        });
        test("inactive owned WPF HWND permits client mouse activation without eating the click", () =>
        {
            var window = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()));
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            Check(!window.IsActive && !window.ShowActivated && !window.Topmost);
            // Query the activation disposition only; no button input, Show or focus request.
            var disposition = SendMessageW(hwnd, 0x0021, hwnd, (nint)((0x0201 << 16) | 1));
            Check(disposition == 1); // MA_ACTIVATE: keep the original WM_LBUTTONDOWN.
            Check(!window.IsActive); window.CloseAutomatically();
        });
        test("one routed click on inactive nudge submits once even when initial focus was denied", () =>
        {
            using var ui = new UiContinuations(); var value = Eligible(); var calls = 0;
            var platform = new Platform { Activation = false };
            var completion = new TaskCompletionSource<DesktopFeasibility.SwitchCompletion>();
            var presenter = new NudgePresenter(command => Task.FromResult(Accept(command, ref value)), () => value,
                _ => { }, platform, (_, _) => { calls++; return completion.Task; });
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult(); var window = presenter.Window!;
            Check(!window.IsActive && platform.Activations == 1);
            var button = (Button)window.FindName("SwitchWork");
            Check(button.Focusable && System.Windows.Input.KeyboardNavigation.GetIsTabStop(button));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(calls == 1 && !button.IsEnabled && window.Result.Text.Contains("Checking"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(calls == 1 && platform.Activations == 1);
            presenter.Close(); completion.SetResult(DesktopFeasibility.SwitchCompletion.Reject("Synthetic rejection.")); ui.Drain();
            Check(presenter.Window is null && platform.Activations == 1 && calls == 1);
        });
        foreach (var outcome in Enum.GetValues<DesktopFeasibility.SwitchOutcome>())
            foreach (var automaticClosure in new[] { false, true })
                test($"explicit switch {outcome} retains typed feedback and closure behavior: automatic closure={automaticClosure}", () =>
                {
                    using var ui = new UiContinuations();
                    var value = Eligible(); var reports = new List<string>(); var calls = 0; var deferrals = 0;
                    var completion = new TaskCompletionSource<DesktopFeasibility.SwitchCompletion>();
                    CancellationToken token = default;
                    var presenter = new NudgePresenter(command =>
                    {
                        if (command.Kind == HostCommandKind.DeferInteractive) deferrals++;
                        return Task.FromResult(Accept(command, ref value));
                    }, () => value, reports.Add, new Platform(), (_, cancellation) => { calls++; token = cancellation; return completion.Task; });
                    presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
                    var window = presenter.Window!;
                    var switchButton = (Button)window.FindName("SwitchWork");
                    Check(!switchButton.IsDefault && AutomationProperties.GetName(switchButton) == "Switch to Work");
                    var task = window.SubmitSwitch();
                    Check(calls == 1 && !switchButton.IsEnabled && !((Button)window.FindName("OneMinute")).IsEnabled);
                    window.SubmitSwitch().GetAwaiter().GetResult(); window.Submit(60_000).GetAwaiter().GetResult(); window.Close();
                    Check(calls == 1 && deferrals == 0 && presenter.Window == window);
                    if (automaticClosure) presenter.Close();
                    Check(token.IsCancellationRequested == automaticClosure);
                    completion.SetResult(new(outcome, "Synthetic result.")); ui.Drain(); task.GetAwaiter().GetResult();
                    Check(presenter.Window is null == (automaticClosure || outcome == DesktopFeasibility.SwitchOutcome.ConfirmedSuccess));
                    Check(reports.Count == (outcome == DesktopFeasibility.SwitchOutcome.ConfirmedSuccess ? 0 : 1));
                    if (!automaticClosure && outcome != DesktopFeasibility.SwitchOutcome.ConfirmedSuccess)
                    {
                        var text = ((TextBlock)window.FindName("Result")).Text;
                        Check(text.Contains("Win+Tab") && text.Contains("Work"));
                        Check(switchButton.IsEnabled == (outcome != DesktopFeasibility.SwitchOutcome.Indeterminate));
                        Check(((Button)window.FindName("OneMinute")).IsEnabled);
                        if (outcome == DesktopFeasibility.SwitchOutcome.Indeterminate)
                        { window.SubmitSwitch().GetAwaiter().GetResult(); Check(calls == 1); }
                    }
                    presenter.Close();
                });
        test("switch exceptions are indeterminate and disable repeat submissions", () =>
        {
            using var ui = new UiContinuations(); var calls = 0;
            var window = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()), () =>
            { calls++; throw new InvalidOperationException("Synthetic failure."); });
            window.SetSwitchAvailability("");
            window.SubmitSwitch().GetAwaiter().GetResult();
            Check(!((Button)window.FindName("SwitchWork")).IsEnabled);
            window.SubmitSwitch().GetAwaiter().GetResult(); Check(calls == 1); window.CloseAutomatically();
        });
        foreach (var reason in new[] { "Enable experimental switching consent for this build in Settings.",
            "Switching is not approved for this Windows build or architecture." })
            test("known unavailable switch stays disabled without progress: " + reason, () =>
            {
                var value = Eligible() with { SwitchingUnavailableReason = reason }; var calls = 0;
                value = value with
                {
                    Settings = reason.Contains("consent", StringComparison.Ordinal)
                    ? value.Settings with { SwitchingConsent = false, SwitchingConsentBuild = "" }
                    : value.Settings with { Build = "26200.9711", SwitchingConsentBuild = "26200.9711" }
                };
                var presenter = new NudgePresenter(command => Task.FromResult(Accept(command, ref value)),
                    () => value, _ => { }, new Platform(), (_, _) =>
                    { calls++; return Task.FromResult(DesktopFeasibility.SwitchCompletion.Unknown("Unexpected call.")); });
                presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
                var window = presenter.Window!;
                Check(!((Button)window.FindName("SwitchWork")).IsEnabled);
                var explanation = (TextBlock)window.FindName("SwitchAvailability");
                Check(explanation.Text.Contains(reason) && explanation.Text.Contains("Win+Tab"));
                Check(window.SwitchWarning.Text.Contains(reason) && AutomationProperties.GetLiveSetting(window.SwitchWarning) == System.Windows.Automation.AutomationLiveSetting.Polite);
                Check(window.SwitchWork.Visibility == Visibility.Collapsed);
                var before = ((TextBlock)window.FindName("Result")).Text;
                window.SubmitSwitch().GetAwaiter().GetResult();
                Check(calls == 0 && ((TextBlock)window.FindName("Result")).Text == before);
                Check(((Button)window.FindName("OneMinute")).IsEnabled && ((Button)window.FindName("FiveMinutes")).IsEnabled);
                value = value with { SwitchingUnavailableReason = "" }; presenter.Refresh();
                Check(((Button)window.FindName("SwitchWork")).IsEnabled);
                value = value with { SwitchingUnavailableReason = reason }; presenter.Refresh();
                Check(!((Button)window.FindName("SwitchWork")).IsEnabled);
                window.Submit(60_000).GetAwaiter().GetResult(); Check(presenter.Window is null);
            });
        test("unexpected destination protocol locks out the nudge and directs Task View inspection", () =>
        {
            var personal = Guid.NewGuid(); var work = Guid.NewGuid(); var other = Guid.NewGuid();
            var baseline = new DesktopFeasibility.Snapshot(new("26200.9457", "X64", 1, 2), DesktopFeasibility.Capabilities.Provider,
                DateTimeOffset.UtcNow, Environment.TickCount64, personal, [new(personal, ""), new(work, ""), new(other, "")], 10, 20, true, null, null);
            var request = new DesktopFeasibility.AuthorizedSwitch(new(Guid.NewGuid(), baseline, work, true), personal, work);
            var completion = DesktopFeasibility.SwitchProtocol.Classify(request,
                new(new(request.Request.RequestId, "WrongDestinationOrUserRace", other, true, 30)), 0);
            var calls = 0;
            var window = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()), () =>
            { calls++; return Task.FromResult(completion); });
            window.SetSwitchAvailability(""); window.SubmitSwitch().GetAwaiter().GetResult();
            Check(completion.Outcome == DesktopFeasibility.SwitchOutcome.Indeterminate);
            Check(!((Button)window.FindName("SwitchWork")).IsEnabled);
            Check(((TextBlock)window.FindName("Result")).Text.Contains("Win+Tab"));
            window.SetSwitchAvailability(""); window.SubmitSwitch().GetAwaiter().GetResult();
            Check(calls == 1); window.CloseAutomatically();
        });
        test("committed event is accepted before any nudge window is constructed", () =>
        {
            var value = Eligible(); var platform = new Platform(); NudgePresenter? presenter = null;
            presenter = new(command =>
            {
                if (command.Kind == HostCommandKind.BeginNudgePresentation) Check(presenter!.Window is null && command.NudgeWindow == 0);
                return Task.FromResult(Accept(command, ref value));
            }, () => value, _ => { }, platform);
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
            Check(platform.Shows == 1 && platform.Activations == 1); presenter.Close();
        });
        test("presenter reuses one HWND and activates only once per new eligible event", () =>
        {
            var value = Eligible(); var platform = new Platform(); var commands = new List<HostCommand>();
            var presenter = new NudgePresenter(command => { commands.Add(command); return Task.FromResult(Accept(command, ref value)); },
                () => value, _ => { }, platform);
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult(); var window = presenter.Window;
            Check(window is not null && window.Owner is null && !window.Topmost && !window.ShowActivated);
            presenter.Refresh(); presenter.Refresh(); Check(platform.Activations == 1);
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult(); Check(platform.Activations == 1);
            presenter.PresentAsync(Event(value, 2)).GetAwaiter().GetResult();
            Check(presenter.Window == window && platform.Activations == 2);
            value = value with { ReminderEpoch = Guid.NewGuid() };
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
            Check(presenter.Window == window && platform.Activations == 3);
            window!.HelpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            value = value with { CanKeepNudgeOpen = false }; presenter.Refresh();
            Check(presenter.Window is null && commands.All(x => x.Kind != HostCommandKind.DeferInteractive));
        });
        foreach (var failure in new[] { "membership", "placement", "foreground", "expired", "stale", "generation", "focus-denied" })
            test("presenter safely handles " + failure, () =>
            {
                var value = Eligible(); var platform = new Platform(); var reminder = Event(value);
                if (failure == "placement") platform.Placement = false;
                if (failure == "foreground") platform.ForegroundWindow = 9;
                if (failure == "expired") reminder = reminder with { ExpiresTick = Environment.TickCount64 - 1 };
                if (failure == "stale") value = value with { PublishedTick = Environment.TickCount64 - 2_001 };
                if (failure == "focus-denied") platform.Activation = false;
                var presenter = new NudgePresenter(command =>
                {
                    var accepted = Accept(command, ref value);
                    if (failure == "generation") value = value with { ReminderGeneration = value.ReminderGeneration + 1, PresentationId = Guid.Empty };
                    return Task.FromResult(failure == "membership" ? HostCommandResult.Failure("wrong desktop") : accepted);
                },
                    () => value, _ => { }, platform);
                presenter.PresentAsync(reminder).GetAwaiter().GetResult(); presenter.Refresh();
                Check(platform.Activations == (failure == "focus-denied" ? 1 : 0));
                if (failure == "focus-denied") Check(presenter.Window is not null);
                else Check(presenter.Window is null);
                if (failure == "placement") Check(presenter.Status.StartsWith("Positioning failed: ContentMeasurement.", StringComparison.Ordinal));
                presenter.Close();
            });
        test("nudge actions are accessible explicit and failed saves stay unsuccessful", () =>
        {
            var durations = new List<long>();
            var window = new NudgeWindow(duration => { durations.Add(duration); return Task.FromResult(HostCommandResult.Failure("Retry save.")); });
            Check(!window.OneMinute.IsDefault && !window.FiveMinutes.IsDefault);
            Check(System.Windows.Input.FocusManager.GetFocusedElement(window) == window.NudgeHeading);
            foreach (var button in new[] { window.OneMinute, window.FiveMinutes })
                Check(button.Focusable && System.Windows.Input.KeyboardNavigation.GetIsTabStop(button) && AutomationProperties.GetName(button).StartsWith("Add "));
            window.Submit(300_000).GetAwaiter().GetResult();
            Check(durations.SequenceEqual(new[] { 300_000L }) && window.Result.Text.Contains("not saved") && window.OneMinute.IsEnabled);
            window.CloseAutomatically(); Check(durations.Count == 1);
        });
        test("dismissal retains the authorized HWND until delayed durable confirmation", () =>
        {
            using var ui = new UiContinuations();
            var durations = new List<long>();
            var pending = new TaskCompletionSource<HostCommandResult>();
            var window = new NudgeWindow(duration => { durations.Add(duration); return pending.Task; });
            var native = new System.Windows.Interop.WindowInteropHelper(window);
            var handle = native.EnsureHandle(); var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Close(); ui.Drain();
            try
            {
                Check(durations.SequenceEqual(new[] { 60_000L }));
                Check(!closed && native.Handle == handle);
                window.Close(); ui.Drain();
                Check(durations.Count == 1 && !window.OneMinute.IsEnabled && !window.FiveMinutes.IsEnabled);
                pending.SetResult(HostCommandResult.Success()); ui.Drain();
                Check(closed && durations.Count == 1);
            }
            finally { window.CloseAutomatically(); }
        });
        foreach (var action in new[] { "X", "Escape", "Alt+F4" })
            foreach (var outcome in new[] { "success", "failure", "exception", "immediate" })
                test($"{action} uses one cancellable dismissal through {outcome} completion", () =>
                {
                    using var ui = new UiContinuations();
                    var pending = new TaskCompletionSource<HostCommandResult>(); var durations = new List<long>();
                    var window = new NudgeWindow(duration =>
                    {
                        durations.Add(duration);
                        return outcome == "immediate" ? Task.FromResult(HostCommandResult.Success()) : pending.Task;
                    });
                    var native = new System.Windows.Interop.WindowInteropHelper(window); native.EnsureHandle();
                    var closed = false; window.Closed += (_, _) => closed = true;
                    void Dismiss()
                    {
                        if (action == "Escape")
                            window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                                System.Windows.Interop.HwndSource.FromHwnd(native.Handle), 0, System.Windows.Input.Key.Escape)
                            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                        // Alt+F4 is translated by Windows to SC_CLOSE. Deliver that
                        // same message to an owned hidden HWND; no global keys/focus.
                        else if (action == "Alt+F4") SendMessageW(native.Handle, 0x112, 0xF060, 0);
                        else window.Close();
                    }
                    try
                    {
                        Dismiss(); Dismiss();
                        Check(!closed && !window.OneMinute.IsEnabled);
                        ui.Drain(); Check(durations.SequenceEqual(new[] { 60_000L }));
                        if (outcome == "immediate") { Check(closed); return; }
                        Dismiss(); ui.Drain(); Check(!closed && durations.Count == 1);
                        if (outcome == "exception") pending.SetException(new InvalidOperationException("Injected command failure"));
                        else pending.SetResult(outcome == "success" ? HostCommandResult.Success() : HostCommandResult.Failure("Retry save."));
                        ui.Drain();
                        Check(closed == (outcome == "success"));
                        if (!closed)
                        {
                            Check(native.Handle != 0 && window.OneMinute.IsEnabled && window.FiveMinutes.IsEnabled && window.Result.Text.Contains("not saved"));
                            window.Submit(60_000).GetAwaiter().GetResult(); Check(durations.Count == 2);
                        }
                        else Check(durations.Count == 1);
                    }
                    finally { window.CloseAutomatically(); }
                });
        foreach (var duration in new[] { 60_000L, 300_000L })
            test($"close during pending Add {duration} cannot close early or submit another command", () =>
            {
                using var ui = new UiContinuations(); var pending = new TaskCompletionSource<HostCommandResult>();
                var durations = new List<long>();
                var window = new NudgeWindow(value => { durations.Add(value); return pending.Task; });
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                var closed = false; window.Closed += (_, _) => closed = true;
                (duration == 60_000 ? window.OneMinute : window.FiveMinutes).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Close(); window.Close(); ui.Drain();
                Check(!closed && durations.SequenceEqual(new[] { duration }));
                pending.SetResult(HostCommandResult.Success()); ui.Drain();
                Check(closed && durations.Count == 1);
            });
        test("automatic closure cancels a queued dismissal without issuing extra time", () =>
        {
            using var ui = new UiContinuations(); var requests = 0;
            var window = new NudgeWindow(_ => { requests++; return Task.FromResult(HostCommandResult.Success()); });
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var closed = false; window.Closed += (_, _) => closed = true;
            window.Close(); window.CloseAutomatically(); window.CloseAutomatically(); ui.Drain();
            Check(closed && requests == 0);
            window.Submit(60_000).GetAwaiter().GetResult(); Check(requests == 0);
        });
        foreach (var invalidation in new[] { "Work", "pause", "lock", "exit" })
            test("automatic " + invalidation + " closure during dismissal suppresses obsolete failure feedback", () =>
            {
                using var ui = new UiContinuations(); var value = Eligible();
                var pending = new TaskCompletionSource<HostCommandResult>(); var failures = new List<string>(); var commands = new List<HostCommand>();
                var presenter = new NudgePresenter(command =>
                {
                    commands.Add(command);
                    return command.Kind == HostCommandKind.DeferInteractive ? pending.Task : Task.FromResult(Accept(command, ref value));
                }, () => value, failures.Add, new Platform());
                presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
                var window = presenter.Window!;
                window.HelpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Close(); ui.Drain();
                value = value with { CanKeepNudgeOpen = false };
                if (invalidation == "Work") value = value with { Context = DesktopScope.Work };
                if (invalidation == "pause") value = value with { State = value.State! with { Paused = true } };
                presenter.Refresh(invalidation == "exit");
                pending.SetResult(HostCommandResult.Failure("Extra time is unavailable in the current context.")); ui.Drain();
                Check(presenter.Window is null && failures.Count == 0 && commands.Count(x => x.Kind == HostCommandKind.DeferInteractive) == 1);
            });
        test("eligible dismissal failure reports once and remains actionable for a successful retry", () =>
        {
            using var ui = new UiContinuations(); var value = Eligible(); var pending = new TaskCompletionSource<HostCommandResult>();
            var failures = new List<string>(); var requests = 0;
            var presenter = new NudgePresenter(command =>
            {
                if (command.Kind != HostCommandKind.DeferInteractive) return Task.FromResult(Accept(command, ref value));
                requests++; Check(command.DurationMs == 60_000 && command.NudgeWindow != 0); return pending.Task;
            }, () => value, failures.Add, new Platform());
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
            var window = presenter.Window!; window.Close(); window.Close(); ui.Drain();
            pending.SetResult(HostCommandResult.Failure("Retry save.")); ui.Drain();
            Check(presenter.Window == window && requests == 1 && failures.Count == 1 && window.OneMinute.IsEnabled);
            Check(window.Result.Text.Contains("Retry save."));
            pending = new(); window.Close(); ui.Drain();
            pending.SetResult(HostCommandResult.Success()); ui.Drain();
            Check(presenter.Window is null && requests == 2 && failures.Count == 1);
        });
        test("storage failure remains explicit after safety closes a pending dismissal", () =>
        {
            using var ui = new UiContinuations(); var value = Eligible();
            var pending = new TaskCompletionSource<HostCommandResult>(); var failures = new List<string>();
            var presenter = new NudgePresenter(command => command.Kind == HostCommandKind.DeferInteractive ? pending.Task :
                Task.FromResult(Accept(command, ref value)), () => value, failures.Add, new Platform());
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
            presenter.Window!.Close(); ui.Drain();
            value = value with { CanKeepNudgeOpen = false, Persistence = OfframpShift.Persistence.PersistenceStatus.PendingCommit };
            presenter.Refresh();
            pending.SetResult(HostCommandResult.Failure("Checkpoint pending. Retry save.")); ui.Drain();
            Check(presenter.Window is null && failures.Count == 1 && failures[0].Contains("Retry save."));
        });
        test("automatic closure during an already dispatched successful request never resurrects a window", () =>
        {
            using var ui = new UiContinuations(); var pending = new TaskCompletionSource<HostCommandResult>(); var requests = 0;
            var window = new NudgeWindow(_ => { requests++; return pending.Task; });
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var closures = 0; window.Closed += (_, _) => closures++;
            window.Close(); ui.Drain(); window.CloseAutomatically();
            pending.SetResult(HostCommandResult.Success()); ui.Drain();
            Check(closures == 1 && requests == 1);
        });
        test("physical monitor placement handles negative origins scaling-sized windows and small work areas", () =>
        {
            Check(NudgePlatform.TryCenter(-2560, -200, 0, 1240, 600, 500, out var area));
            Check(area == (-1580, 270, 600, 500));
            Check(NudgePlatform.TryCenter(0, 0, 300, 200, 800, 600, out area) && area == (0, 0, 300, 200));
            Check(!NudgePlatform.TryCenter(0, 0, 0, 200, 400, 300, out _));
            Check(NudgePlatform.TryCenter(3440, -500, 5600, 3268, 600, 700, out area) && area == (4220, 1034, 600, 700));
        });
        test("DPI placement prohibits nested activation and restores exact original style on every outcome", () =>
        {
            foreach (var outcome in new[] { "success", "failure", "exception", "apply-denied", "restore-denied" })
            {
                const int original = 0x40000;
                var style = original; var called = false;
                void Write(int value)
                {
                    if (outcome == "apply-denied" || outcome == "restore-denied" && value == original) return;
                    style = value;
                }
                Task<bool> Position()
                {
                    called = true; Check((style & 0x08000000) != 0);
                    if (outcome == "exception") throw new ApplicationException("synthetic DPI positioning failure");
                    return Task.FromResult(outcome != "failure");
                }
                try
                {
                    var result = NudgePlatform.WithoutImplicitActivationAsync(() => style, Write, Position).GetAwaiter().GetResult();
                    Check(result.Placed == (outcome == "success"));
                    Check(result.Restored == (outcome != "restore-denied"));
                }
                catch (ApplicationException) when (outcome == "exception") { }
                Check(called == (outcome != "apply-denied"));
                if (outcome != "restore-denied") Check(style == original);
            }
        });
        test("a real never-shown HWND measures visible content instead of collapsed Window", () =>
        {
            var window = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()));
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            window.Measure(new(400, 800));
            Check(!window.IsVisible && window.DesiredSize.Height == 0);
            var height = NudgePlatform.MeasureContentHeight(window, 384, 760);
            Check(height > 0 && height <= 760);
            Check(NudgePlatform.TryCenter(0, 0, 1920, 1080, 400, (int)Math.Ceiling(height) + 40, out _));
            window.CloseAutomatically();
        });
        test("first show is nonactivating preparation followed by strict final authorization", () =>
        {
            var value = Eligible(); var platform = new Platform(); var phases = new List<bool>();
            var presenter = new NudgePresenter(command =>
            {
                phases.Add(command.Kind == HostCommandKind.BeginNudgePresentation);
                if (phases.Count == 2) Check(platform.Shows == 1 && platform.Activations == 0);
                return Task.FromResult(Accept(command, ref value));
            }, () => value, _ => { }, platform);
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
            Check(phases.SequenceEqual(new[] { true, false }) && platform.Activations == 1);
            presenter.PresentAsync(Event(value, 2)).GetAwaiter().GetResult();
            Check(phases.SequenceEqual(new[] { true, false, true, false }));
            presenter.Close();
        });
        test("unregistered candidate after nonactivating show is closed without focus and reported locally", () =>
        {
            var value = Eligible(); var platform = new Platform();
            var presenter = new NudgePresenter(command => Task.FromResult(command.Kind == HostCommandKind.BeginNudgePresentation ? Accept(command, ref value) :
                HostCommandResult.Failure("Desktop check: CandidateIdentityUnavailable.")), () => value, _ => { }, platform);
            presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
            Check(platform.Shows == 1 && platform.Activations == 0 && presenter.Window is null);
            Check(presenter.Status.Contains("after show") && presenter.Status.Contains("CandidateIdentityUnavailable"));
        });
        foreach (var foreground in new[] { "candidate", "original", "external" })
            test("presentation transition checks owned foreground independently of accounting generation: " + foreground, () =>
            {
                var value = Eligible(); var platform = new Platform();
                platform.DuringPresentation = window =>
                {
                    value = value with { ReminderGeneration = value.ReminderGeneration + 1 };
                    platform.ForegroundWindow = foreground == "candidate" ? new System.Windows.Interop.WindowInteropHelper(window).Handle :
                        foreground == "original" ? 1 : 99;
                };
                var presenter = new NudgePresenter(command => Task.FromResult(Accept(command, ref value)), () => value, _ => { }, platform);
                presenter.PresentAsync(Event(value)).GetAwaiter().GetResult();
                Check(platform.Activations == (foreground == "external" ? 0 : 1));
                Check((presenter.Window is not null) == (foreground != "external"));
                presenter.Close();
            });
        test("nonactivation guard spans asynchronous DPI layout and restores before completion", () =>
        {
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                var style = 0x40000; var layout = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var operation = NudgePlatform.WithoutImplicitActivationAsync(() => style, v => style = v, () => layout.Task);
                Check(!operation.IsCompleted && (style & 0x08000000) != 0);
                layout.SetResult(true);
                var result = operation.GetAwaiter().GetResult();
                Check(result.Placed && result.Restored && style == 0x40000);
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        });
        test("repeat Settings validates whole minutes and submits independent durable edit", () =>
        {
            var commands = new List<HostCommand>();
            var window = new MainWindow(command => { commands.Add(command); return Task.FromResult(HostCommandResult.Success()); }, () => Task.CompletedTask, () => Task.CompletedTask);
            window.Apply(snapshot());
            Check(AutomationProperties.GetName(window.RepeatInput) == "Reminder repeat interval in whole minutes");
            Check((string)window.SaveRepeatButton.Content == "Save repeat interval");
            foreach (var bad in new[] { "0", "61", "1.5", "bad" })
            { window.RepeatInput.Text = bad; window.SaveRepeatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            Check(commands.Count == 0);
            window.RepeatInput.Text = "60"; window.SaveRepeatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(commands.Count == 1 && commands[0].Kind == HostCommandKind.ChangeInteractiveRepeat && commands[0].DurationMs == 3_600_000);
        });
    }
}
