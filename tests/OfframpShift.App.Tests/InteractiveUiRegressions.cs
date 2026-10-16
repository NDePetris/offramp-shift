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
        public int Shows, Activations;
        public bool Place(NudgeWindow window, nint foreground) => Placement;
        public void Show(NudgeWindow window) => Shows++;
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
        test("presenter reuses one HWND and activates only once per new eligible event", () =>
        {
            var value = Eligible(); var platform = new Platform(); var commands = new List<HostCommand>();
            var presenter = new NudgePresenter(command => { commands.Add(command); return Task.FromResult(HostCommandResult.Success()); },
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
                var presenter = new NudgePresenter(_ =>
                {
                    if (failure == "generation") value = value with { ReminderGeneration = value.ReminderGeneration + 1 };
                    return Task.FromResult(failure == "membership" ? HostCommandResult.Failure("wrong desktop") : HostCommandResult.Success());
                },
                    () => value, _ => { }, platform);
                presenter.PresentAsync(reminder).GetAwaiter().GetResult(); presenter.Refresh();
                Check(platform.Activations == (failure == "focus-denied" ? 1 : 0));
                if (failure == "focus-denied") Check(presenter.Window is not null);
                else Check(presenter.Window is null);
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
        });
        test("repeat Settings validates whole minutes and submits independent durable edit", () =>
        {
            var commands = new List<HostCommand>();
            var window = new MainWindow(command => { commands.Add(command); return Task.FromResult(HostCommandResult.Success()); }, () => Task.CompletedTask, () => Task.CompletedTask);
            window.Apply(snapshot());
            foreach (var bad in new[] { "0", "61", "1.5", "bad" })
            { window.RepeatInput.Text = bad; window.SaveRepeatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            Check(commands.Count == 0);
            window.RepeatInput.Text = "60"; window.SaveRepeatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(commands.Count == 1 && commands[0].Kind == HostCommandKind.ChangeInteractiveRepeat && commands[0].DurationMs == 3_600_000);
        });
    }
}
