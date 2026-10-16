using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;

internal static class InteractiveUiRegressions
{
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
                Settings = value.Settings with { ReminderStyle = ReminderStyle.InteractiveWindow },
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
        test("user closure grants one minute and extension submission cannot duplicate on closure", () =>
        {
            var durations = new List<long>();
            var window = new NudgeWindow(duration => { durations.Add(duration); return Task.FromResult(HostCommandResult.Success()); });
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            window.Close(); Check(durations.SequenceEqual(new[] { 60_000L }));
            var pending = new TaskCompletionSource<HostCommandResult>();
            var second = new NudgeWindow(duration => { durations.Add(duration); return pending.Task; });
            new System.Windows.Interop.WindowInteropHelper(second).EnsureHandle();
            var request = second.Submit(300_000);
            second.Submit(60_000).GetAwaiter().GetResult();
            Check(!second.OneMinute.IsEnabled && !second.FiveMinutes.IsEnabled);
            second.Close(); Check(durations.SequenceEqual(new[] { 60_000L, 300_000L }));
            pending.SetResult(HostCommandResult.Success()); request.GetAwaiter().GetResult();
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
            Check((string)window.SaveRepeatButton.Content == "Save reminder repeat interval");
            foreach (var bad in new[] { "0", "61", "1.5", "bad" })
            { window.RepeatInput.Text = bad; window.SaveRepeatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            Check(commands.Count == 0);
            window.RepeatInput.Text = "60"; window.SaveRepeatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(commands.Count == 1 && commands[0].Kind == HostCommandKind.ChangeInteractiveRepeat && commands[0].DurationMs == 3_600_000);
        });
    }
}
