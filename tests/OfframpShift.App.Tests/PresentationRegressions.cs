using System.Windows;
using System.IO;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;

internal static class PresentationRegressions
{
    internal static void Run(Action<string, Action> test, Func<HostSnapshot> snapshot)
    {
        void Check(bool value) { if (!value) throw new InvalidOperationException("Presentation assertion failed."); }
        test("timer hero shares exact minute rounding and latched exhaustion with tray", () =>
        {
            var model = new DashboardModel(); var value = snapshot();
            foreach (var usage in new[] { 0L, 1, 59_999, 60_000, value.Configuration!.AllowanceMs - 1, value.Configuration.AllowanceMs, long.MaxValue })
            {
                model.Apply(value with { State = value.State! with { UsageMs = usage, Exhausted = usage >= value.Configuration.AllowanceMs } }, value.PublishedTick);
                Check(model.Tray.Allowance.Contains(model.HeroLabel) && model.Tray.Allowance.Contains(model.HeroValue));
                Check(model.Tray.Break.Contains(model.BreakValue) && model.Tray.Tooltip.Length <= 63);
            }
            model.Apply(value with { State = value.State! with { Exhausted = true, UsageMs = 10 }, Configuration = value.Configuration with { AllowanceMs = long.MaxValue } }, value.PublishedTick);
            Check(model.HeroLabel == "Allowance exhausted" && model.ShowExhaustionExplanation && model.HeroValue == "—");
            Check(model.Tray.Tooltip == "Allowance exhausted");
            model.Apply(value with { State = value.State! with { UsageMs = value.Configuration.AllowanceMs - 1 } }, value.PublishedTick);
            Check(model.HeroValue == "<1 min");
            Check(DashboardModel.Duration(long.MaxValue).EndsWith(" min", StringComparison.Ordinal));
            Check(TraySummary.Duration(0) == "0 min" && TraySummary.Duration(1) == "<1 min");
        });
        test("committed extension and repeat are exclusive and never extrapolate", () =>
        {
            var model = new DashboardModel(); var value = snapshot() with { Settings = snapshot().Settings with { ReminderStyle = ReminderStyle.InteractiveWindow } };
            foreach (var intervention in Enum.GetValues<InterventionState>())
            {
                var next = value with { State = value.State! with { Exhausted = true, Intervention = intervention, ReminderRemainingMs = 61_000, SnoozeRemainingMs = 299_999 } };
                model.Apply(next, next.PublishedTick);
                Check(model.ShowReminder == (intervention is InterventionState.Nudging or InterventionState.PersonalDeferred));
                if (model.ShowReminder) Check(model.ReminderValue == (intervention == InterventionState.PersonalDeferred ? "4 min" : "1 min"));
                var hero = model.HeroValue; var reminder = model.ReminderValue; var progress = model.BreakProgress;
                model.Apply(next, next.PublishedTick + 2_001);
                Check(model.HeroValue == hero && model.ReminderValue == reminder && model.BreakProgress == progress);
                Check(model.RecordedQualifier == "Last recorded");
            }
            model.Apply(value with { State = value.State! with { Exhausted = true, Intervention = InterventionState.Nudging }, Settings = value.Settings with { ReminderStyle = ReminderStyle.Off } }, value.PublishedTick);
            Check(!model.ShowReminder);
        });
        test("host update delay is separate from observations and previously observed context", () =>
        {
            var model = new DashboardModel(); var value = snapshot();
            model.Apply(value, value.PublishedTick + 2_001);
            Check(model.ContextLabel == "Unknown" && model.Health == "Monitoring updates delayed");
            model.Apply(value, value.PublishedTick);
            foreach (var tick in new[] { value.PublishedTick - 1, value.PublishedTick + 2_001 })
            {
                model.Apply(value, tick);
                Check(model.ContextLabel == "Last observed: Personal" && !model.CanPause && model.RecordedQualifier == "Last recorded");
                Check(model.Tray.Status == model.Health && model.Tray.Context == model.ContextLabel);
            }
            model.Apply(value with { Available = false }, value.PublishedTick);
            Check(model.Health == "Observations unavailable" && model.ContextLabel == "Personal desktop" && model.CanPause);
            model.Apply(value with { State = value.State! with { Paused = true } }, value.PublishedTick);
            Check(model.CanResume && model.RecordedQualifier == "Last recorded" && model.Health.StartsWith("Monitoring paused", StringComparison.Ordinal));
            model.Apply(value with { Context = DesktopScope.Work, State = value.State! with { BreakProgressMs = 120_000 } }, value.PublishedTick);
            Check(model.BreakExplanation.Contains("Work") && model.Tray.Context == "Work desktop");
            model.Apply(value with { Context = DesktopScope.Work, State = value.State! with { BreakCompleted = true } }, value.PublishedTick);
            Check(model.BreakExplanation == "Break completed; allowance reset");
            model.Apply(value with { Configuration = value.Configuration! with { Accounting = AccountingMode.CertifiedOnly } }, value.PublishedTick);
            Check(model.Health == "Monitoring is not started" && model.RecordedQualifier == "Last recorded");
        });
        test("only Ready pairs enter cache; epochs and skipped publications cannot resurrect totals", () =>
        {
            var model = new DashboardModel(); var value = snapshot() with { Sequence = 10, AccountingPresentationEpoch = Guid.NewGuid() };
            foreach (var status in Enum.GetValues<PersistenceStatus>().Where(x => x != PersistenceStatus.Ready))
            {
                var empty = new DashboardModel(); empty.Apply(value with { Persistence = status }, value.PublishedTick);
                Check(empty.Recorded is null && empty.HeroValue == "—" && empty.Tray.Allowance == "Recorded usage unavailable");
            }
            model.Apply(value, value.PublishedTick);
            var recorded = model.Recorded;
            model.Apply(value with { Sequence = 11, Persistence = PersistenceStatus.PendingCommit, State = null, Configuration = null }, value.PublishedTick);
            Check(model.Recorded == recorded && model.RecordedQualifier == "Last recorded");
            var boundary = value with { Sequence = 15, AccountingPresentationEpoch = Guid.NewGuid(), Persistence = PersistenceStatus.PendingCommit, State = null, Configuration = null };
            model.Apply(boundary, value.PublishedTick);
            Check(model.Recorded is null && model.Tray.Allowance == "Recorded usage unavailable" && model.ContextLabel == "Personal desktop");
            model.Apply(value, value.PublishedTick); Check(model.Snapshot == boundary && model.Recorded is null);
            var restored = value with { Sequence = 16, AccountingPresentationEpoch = boundary.AccountingPresentationEpoch, State = value.State! with { UsageMs = 0 } };
            model.Apply(restored, value.PublishedTick); Check(model.Recorded?.State.UsageMs == 0 && model.Recorded.Sequence == 16);
            foreach (var status in new[] { PersistenceStatus.RecoveryRequired, PersistenceStatus.Incompatible })
            {
                model.Apply(restored with { Sequence = 17, Persistence = status }, value.PublishedTick);
                Check(model.Recorded is null);
                model.Apply(restored with { Sequence = 18, Persistence = PersistenceStatus.Blocked }, value.PublishedTick);
                Check(model.Recorded is null);
            }
        });
        test("owner termination clears shared authority and refuses subsequent stale owner publications", () =>
        {
            var model = new DashboardModel(); var value = snapshot(); model.Apply(value, value.PublishedTick);
            model.MarkUnavailable("Owner stopped without a final authoritative state.");
            model.Apply(value with { Sequence = 999 }, value.PublishedTick);
            Check(model.Recorded is null && model.HeroValue == "—" && model.ContextLabel == "Unknown");
            Check(!model.CanPause && !model.CanResume && model.Tray.Allowance == "Recorded usage unavailable");
        });
        test("ordinary commands reserve before await, report delay, reject duplicates and retain confirmed facts", () =>
        {
            long tick = 10; var coordinator = new CommandCoordinator(() => tick); var completion = new TaskCompletionSource<HostCommandResult>();
            var calls = 0; var changes = 0; coordinator.Changed += () => changes++;
            var model = new DashboardModel(); var value = snapshot(); model.Apply(value, value.PublishedTick);
            coordinator.Changed += () => model.SetCommandPending(coordinator.IsPending);
            var pending = coordinator.ExecuteAsync(new(HostCommandKind.Pause), _ => { calls++; return completion.Task; });
            Check(coordinator.IsPending && coordinator.Message.Contains("Pause requested") && model.Snapshot!.State!.Paused == false && !model.CanPause);
            var duplicate = coordinator.ExecuteAsync(new(HostCommandKind.Resume), _ => { calls++; return Task.FromResult(HostCommandResult.Success()); }).GetAwaiter().GetResult();
            Check(!duplicate.Succeeded && calls == 1);
            tick = 5_010; coordinator.RefreshWaiting(); var once = changes; coordinator.RefreshWaiting();
            Check(coordinator.Message.Contains("Still waiting") && changes == once);
            completion.SetResult(HostCommandResult.Failure("Save not acknowledged.")); var result = pending.GetAwaiter().GetResult();
            Check(!result.Succeeded && !coordinator.IsPending && coordinator.Message == "Save not acknowledged." && model.CanPause);
            Check(model.Snapshot!.State!.Paused == false);
            result = coordinator.ExecuteAsync(new(HostCommandKind.Resume), _ => Task.FromResult(HostCommandResult.Success())).GetAwaiter().GetResult();
            Check(result.Succeeded && result.Message.Contains("fresh evidence"));
            result = coordinator.ExecuteAsync(new(HostCommandKind.Pause), _ => throw new IOException()).GetAwaiter().GetResult();
            Check(!result.Succeeded && !coordinator.IsPending);
        });
    }

    internal static void Views(Action<string, Action> test, Func<HostSnapshot> snapshot)
    {
        test("compact native shell routes overview and details while preserving existing editors", () =>
        {
            var window = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            window.Apply(snapshot());
            if (window.ActionResult.TextWrapping != TextWrapping.Wrap) throw new InvalidOperationException("Command feedback must wrap.");
            if (window.Width != 440 || window.Height != 540 || window.MinWidth != 400 || window.MinHeight != 500) throw new InvalidOperationException("Window size");
            window.OpenSettings();
            if (window.CurrentPage != "Settings" || window.SettingsOverview.Visibility != Visibility.Visible) throw new InvalidOperationException("Overview");
            var panel = (Panel)window.SettingsOverview.Content;
            foreach (var button in panel.Children.OfType<Button>())
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.CurrentPage != (string)button.Tag || window.SettingsDetail.Visibility != Visibility.Visible) throw new InvalidOperationException("Detail route");
                window.NavigateBack(); if (window.CurrentPage != "Settings") throw new InvalidOperationException("Back detail");
            }
            window.NavigateBack(); if (window.CurrentPage != "Home") throw new InvalidOperationException("Back Home");
            if (AutomationProperties.GetName(window.SettingsButton) != "Settings" || UIElementAutomationPeer.CreatePeerForElement(window.MonitoringButton)?.GetAutomationControlType() != AutomationControlType.Button) throw new InvalidOperationException("Automation");
            foreach (var progress in ((Panel)window.HomePage.Content).Children.OfType<ProgressBar>())
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(progress);
                if (peer?.GetPattern(PatternInterface.RangeValue) is null || peer.GetName().Length == 0) throw new InvalidOperationException("Progress automation");
            }
            foreach (var dark in new[] { false, true }) foreach (var contrast in new[] { false, true })
            {
                window.ApplyBrand(dark, contrast);
                var root = (FrameworkElement)window.Content;
                root.Measure(new(360, 440)); root.Arrange(new(0, 0, 360, 440)); root.UpdateLayout();
                if (!double.IsFinite(root.DesiredSize.Height) || window.HomePage.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled) throw new InvalidOperationException("Layout");
            }
        });
    }
}
