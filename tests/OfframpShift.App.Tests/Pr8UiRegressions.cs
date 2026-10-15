using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;

internal static class Pr8UiRegressions
{
    internal static void Run(Action<string, Action> test, Func<HostSnapshot> snapshot)
    {
        void Check(bool value) { if (!value) throw new InvalidOperationException("PR 8 UI assertion failed."); }
        test("reminder clicks cannot open or dismiss retained error feedback and errors have priority", () =>
        {
            var balloons = new TrayBalloonState(); var feedback = new TrayActionFeedback();
            Check(balloons.BeginReminder(false) && !balloons.ClickOpensError);
            Check(!balloons.BeginReminder(false));
            Check(feedback.ExecuteAsync(new(HostCommandKind.Pause), _ => Task.FromResult(HostCommandResult.Failure("Storage unavailable."))).Result);
            Check(!balloons.BeginError() && !balloons.ClickOpensError);
            Check(!balloons.BeginReminder(feedback.Message.Length > 0));
            Check(feedback.Message == "Storage unavailable.");
            balloons.Closed(); Check(balloons.BeginError() && balloons.ClickOpensError);
            Check(!balloons.BeginError());
            Check(!balloons.BeginReminder(false) && balloons.ClickOpensError);
            balloons.Closed(); feedback.Dismiss();
            Check(balloons.BeginReminder(false) && !balloons.ClickOpensError);
            balloons.Closed(); Check(!balloons.ClickOpensError);
        });
        test("reset confirmation cancellation submits nothing and failure stays unsuccessful", () =>
        {
            var calls = 0; var confirmed = false;
            var window = new MainWindow(command =>
            {
                calls++; Check(command.Kind == HostCommandKind.ResetUsage && command.Confirmed);
                return Task.FromResult(HostCommandResult.Failure("Save not acknowledged. Retry save."));
            }, () => Task.CompletedTask, () => Task.CompletedTask, () => confirmed);
            window.Apply(snapshot());
            window.ResetUsageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(calls == 0);
            confirmed = true;
            window.ResetUsageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(calls == 1 && window.SettingsResult.Text.Contains("Retry save") && !window.SettingsResult.Text.Contains("completed"));
            Check(MainWindow.ResetUsageConfirmation.Contains("break progress") && MainWindow.ResetUsageConfirmation.Contains("Pause"));
        });
        test("reset eligibility is Ready-only while reminder editing follows settings availability", () =>
        {
            var model = new DashboardModel(); var value = snapshot();
            foreach (var persistence in Enum.GetValues<PersistenceStatus>())
            {
                model.Apply(value with { Persistence = persistence }, value.PublishedTick);
                Check(model.CanResetUsage == (persistence == PersistenceStatus.Ready));
                Check(model.CanEditReminders);
            }
            foreach (var status in Enum.GetValues<SettingsStatus>())
            {
                model.Apply(value with { SettingsStatus = status }, value.PublishedTick);
                Check(model.CanEditReminders == (status is SettingsStatus.Ready or SettingsStatus.Missing));
            }
            model.Apply(value, value.PublishedTick + 2_001); Check(!model.CanResetUsage && !model.CanEditReminders);
            model.MarkUnavailable("Owner stopped."); Check(!model.CanResetUsage && !model.CanEditReminders);
        });
        test("native reminder controls bind saved values, report save failure and expose accessible names", () =>
        {
            var commands = new List<HostCommand>();
            var window = new MainWindow(command =>
            {
                commands.Add(command);
                return Task.FromResult(HostCommandResult.Failure("Windows settings could not be saved."));
            }, () => Task.CompletedTask, () => Task.CompletedTask);
            var value = snapshot(); window.Apply(value);
            var root = (FrameworkElement)window.Content;
            root.Measure(new(820, 760)); root.Arrange(new(0, 0, 820, 760)); root.UpdateLayout();
            Check(window.RemindersInput.IsChecked == false);
            window.Apply(value with { Settings = value.Settings with { BreakRemindersEnabled = true } });
            Check(window.RemindersInput.IsChecked == true);
            window.SaveRemindersButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(commands.Count == 1 && commands[0].Kind == HostCommandKind.ChangeBreakReminders && commands[0].BreakRemindersEnabled);
            Check(window.SettingsResult.Text.Contains("could not be saved") && window.ActionResult.Text == window.SettingsResult.Text);
            Check(AutomationProperties.GetName(window.RemindersInput) == "Enable break reminders");
            var checkPeer = UIElementAutomationPeer.CreatePeerForElement(window.RemindersInput);
            Check(checkPeer?.GetAutomationControlType() == AutomationControlType.CheckBox);
            foreach (var button in new[] { window.ResetUsageButton, window.SaveRemindersButton })
            {
                Check(button.Focusable && System.Windows.Input.KeyboardNavigation.GetIsTabStop(button));
                var peer = UIElementAutomationPeer.CreatePeerForElement(button);
                Check(peer is not null && peer.GetAutomationControlType() == AutomationControlType.Button && peer.GetName().Length > 0);
            }
            Check(AutomationProperties.GetHelpText(window.ResetUsageButton).Contains("confirmation"));
            window.Apply(value with { Persistence = PersistenceStatus.PendingCommit }); root.UpdateLayout();
            Check(!window.ResetUsageButton.IsEnabled);
            window.Apply(value, ownerStopped: true); root.UpdateLayout();
            Check(!window.RemindersInput.IsEnabled && !window.SaveRemindersButton.IsEnabled);
        });
    }
}
