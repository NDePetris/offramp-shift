using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;

internal static class SettingsDiagnosticsRegressions
{
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Settings/Diagnostics assertion failed."); }
    private static void Complete(Task task)
    {
        while (!task.IsCompleted) Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        task.GetAwaiter().GetResult();
    }
    private static void Detail(MainWindow window, string page)
    {
        window.OpenSettings();
        ((Panel)window.SettingsOverview.Content).Children.OfType<Button>().First(button => (string)button.Tag == page).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    internal static void Run(Action<string, Action> test, Func<HostSnapshot> snapshot)
    {
        test("dirty settings survive publications and relevant conflict while unrelated fields refresh", () =>
        {
            var window = new MainWindow(_ => Task.FromResult(HostCommandResult.Failure("Conflict retained.")), () => Task.CompletedTask, () => Task.CompletedTask);
            var seed = snapshot(); window.Apply(seed); Detail(window, "Monitoring");
            window.AllowanceInput.Text = "47";
            window.AllowanceInput.Select(0, 1);
            window.Apply(seed with { Sequence = 2, Settings = seed.Settings with { Theme = AppTheme.Dark } });
            Check(window.AllowanceInput.Text == "47" && window.ThemeInput.SelectedItem is AppTheme.Dark && !window.SettingsResult.Text.Contains("changed"));
            Check(window.AllowanceInput.SelectionStart == 0 && window.AllowanceInput.SelectionLength == 1);
            window.Apply(seed with { Sequence = 3, Configuration = seed.Configuration! with { AllowanceMs = 600_000 } });
            Check(window.AllowanceInput.Text == "47" && window.SettingsResult.Text.Contains("retained"));
            Check(!window.SaveDraftAsync(SettingsGroup.Durations).Result && window.HasUnsavedChanges);
            window.Apply(seed with { Sequence = 4, Configuration = seed.Configuration! with { AllowanceMs = 600_000 } });
            Check(window.AllowanceInput.Text == "47" && window.SettingsResult.Text == "Conflict retained.");
            window.ChooseUnsaved = () => UnsavedChoice.KeepEditing; window.NavigateBack(); Check(window.CurrentPage == "Monitoring");
            window.ChooseUnsaved = () => UnsavedChoice.Discard; window.NavigateBack();
            Check(window.CurrentPage == "Settings" && !window.HasUnsavedChanges && window.AllowanceInput.Text == "10");
        });
        test("save confirmation protects Back close and Exit without abandoning or duplicating pending save", () =>
        {
            var completion = new TaskCompletionSource<HostCommandResult>(); var calls = 0;
            var window = new MainWindow(command => { calls++; Check(command.Expected is not null); return completion.Task; }, () => Task.CompletedTask, () => Task.CompletedTask);
            window.Apply(snapshot()); Detail(window, "Monitoring"); window.AllowanceInput.Text = "42";
            window.ChooseUnsaved = () => UnsavedChoice.Save;
            var leave = window.ProtectDraftsAsync();
            Check(calls == 1 && window.SavePending && !leave.IsCompleted);
            Check(window.SettingsResult.Text.Contains("Saving monitoring settings"));
            window.NavigateBack(); window.OpenSettings();
            Check(window.CurrentPage == "Monitoring" && !window.ProtectDraftsAsync().Result);
            Check(!window.SaveDraftAsync(SettingsGroup.Durations).Result && calls == 1);
            completion.SetResult(HostCommandResult.Success()); Complete(leave);
            Check(leave.Result && !window.HasUnsavedChanges && window.SettingsResult.Text == "Changes saved.");
            window.NavigateBack(); Check(window.CurrentPage == "Settings");
        });
        test("failed save and validation retain draft while Cancel explicitly discards", () =>
        {
            var calls = 0;
            var window = new MainWindow(_ => { calls++; return Task.FromResult(HostCommandResult.Failure("Storage failed; Retry save.")); }, () => Task.CompletedTask, () => Task.CompletedTask);
            window.Apply(snapshot()); Detail(window, "Monitoring");
            window.AllowanceInput.Text = "invalid";
            Check(!window.SaveDraftAsync(SettingsGroup.Durations).Result && calls == 0 && window.HasUnsavedChanges);
            window.AllowanceInput.Text = "42"; window.ChooseUnsaved = () => UnsavedChoice.Save;
            Check(!window.ProtectDraftsAsync().Result && calls == 1 && window.AllowanceInput.Text == "42");
            Check(window.SettingsResult.Text.Contains("Retry save"));
            Check(System.Windows.Automation.AutomationProperties.GetHelpText(window.AllowanceInput).Length == 0);
            window.MonitoringCancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!window.HasUnsavedChanges && window.CurrentPage == "Settings");
        });
        test("unavailable owner preserves drafts and still permits explicit discard", () =>
        {
            var window = new MainWindow(_ => throw new InvalidOperationException("must not submit"), () => Task.CompletedTask, () => Task.CompletedTask);
            window.Apply(snapshot()); Detail(window, "Appearance"); window.ThemeInput.SelectedItem = AppTheme.Dark;
            window.Apply(snapshot(), ownerStopped: true);
            Check(!window.SaveDraftAsync(SettingsGroup.Appearance).Result && window.HasUnsavedChanges);
            window.ChooseUnsaved = () => UnsavedChoice.Discard;
            Check(window.ProtectDraftsAsync().Result && !window.HasUnsavedChanges);
        });
        test("native unsaved dialog exposes explicit choices and disables unavailable Save", () =>
        {
            var owner = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask)
            { ShowActivated = false, ShowInTaskbar = false };
            owner.Show();
            try
            {
                foreach (var selected in new[] { UnsavedChoice.Save, UnsavedChoice.Discard, UnsavedChoice.KeepEditing })
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                    {
                        var dialog = owner.OwnedWindows.OfType<Window>().Single(window => window.Title == "Unsaved changes");
                        var buttons = ((Panel)dialog.Content).Children.OfType<Button>().ToArray();
                        Check(buttons.Select(button => (string)button.Content).SequenceEqual(["Save changes", "Discard changes", "Keep editing"]));
                        Check(buttons[2].IsCancel && buttons[0].IsEnabled == (selected == UnsavedChoice.Save));
                        buttons[(int)selected].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    });
                    Check(UnsavedChangesDialog.Choose(owner, selected == UnsavedChoice.Save) == selected);
                }
            }
            finally { owner.Hide(); }
        });
        test("reminder saves retain separate expected values and failure boundaries", () =>
        {
            var commands = new List<HostCommand>();
            var window = new MainWindow(command => { commands.Add(command); return Task.FromResult(command.Kind == HostCommandKind.ChangeBreakReminders ? HostCommandResult.Success() : HostCommandResult.Failure("Repeat not saved.")); }, () => Task.CompletedTask, () => Task.CompletedTask);
            var seed = snapshot(); window.Apply(seed); Detail(window, "Reminders");
            window.RemindersInput.SelectedIndex = 2; window.RepeatInput.Text = "9";
            Check(window.SaveDraftAsync(SettingsGroup.ReminderStyle).Result && window.HasUnsavedChanges);
            Check(!window.SaveDraftAsync(SettingsGroup.Repeat).Result && window.RepeatInput.Text == "9");
            Check(commands.Count == 2 && commands[0].Expected!.ReminderStyle == seed.Settings.ReminderStyle && commands[1].Expected!.RepeatMs == seed.Configuration!.InteractiveRepeatNudgeMs);
        });
        test("PendingCommit disables duration save without losing draft and retains exact retry route", () =>
        {
            var commands = new List<HostCommand>();
            var window = new MainWindow(command => { commands.Add(command); return Task.FromResult(HostCommandResult.Success()); }, () => Task.CompletedTask, () => Task.CompletedTask);
            var seed = snapshot(); window.Apply(seed); Detail(window, "Monitoring"); window.AllowanceInput.Text = "42";
            window.Apply(seed with { Sequence = 2, Persistence = PersistenceStatus.PendingCommit, Configuration = null, State = null });
            Check(!window.SaveDraftAsync(SettingsGroup.Durations).Result && window.AllowanceInput.Text == "42");
            Complete(window.ExecuteDiagnosticAsync(HostCommandKind.RetrySave));
            Check(commands.Count == 1 && commands[0].Kind == HostCommandKind.RetrySave && commands[0].Expected is null);
            window.ChooseUnsaved = () => UnsavedChoice.Discard;
            Check(window.ProtectDraftsAsync().Result && !window.HasUnsavedChanges && window.Model.Snapshot!.Persistence == PersistenceStatus.PendingCommit);
        });
        test("UI preferences default off round-trip strictly and preserve malformed future and externally changed files", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "offramp-ui-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new UiPreferences(directory); store.Load(); Check(!store.ShowDiagnostics && store.Error.Length == 0);
                Check(store.Save(true)); var reload = new UiPreferences(directory); reload.Load(); Check(reload.ShowDiagnostics);
                var path = Path.Combine(directory, "ui-preferences.json");
                var confirmed = File.ReadAllBytes(path);
                store.BeforeReplace = () => throw new IOException("Injected atomic replace failure");
                Check(!store.Save(false) && store.ShowDiagnostics && File.ReadAllBytes(path).SequenceEqual(confirmed));
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0);
                foreach (var invalid in new[] { "{broken", "{\"Version\":2,\"ShowDiagnostics\":true}", "{\"Version\":1}", "{\"Version\":1,\"ShowDiagnostics\":1}", "{\"Version\":1,\"ShowDiagnostics\":true,\"Other\":0}", "{\"Version\":1,\"Version\":1,\"ShowDiagnostics\":true}", "[]" })
                {
                    File.WriteAllText(path, invalid); var broken = new UiPreferences(directory); broken.Load();
                    Check(!broken.ShowDiagnostics && broken.Error.Length > 0 && !broken.Save(true) && File.ReadAllText(path) == invalid);
                }
                File.WriteAllBytes(path, confirmed); reload.Load(); File.WriteAllText(path, "{broken");
                Check(!reload.Save(false) && reload.ShowDiagnostics && File.ReadAllText(path) == "{broken");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        test("Diagnostics lifecycle is opt-in one-instance nonmodal and failed disabling preserves the window", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "offramp-diag-" + Guid.NewGuid().ToString("N"));
            var main = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            main.Apply(snapshot()); var coordinator = new CommandCoordinator();
            var preferences = new UiPreferences(directory); preferences.Load(); var creates = 0;
            using var companion = new DiagnosticsCompanion(preferences, () => { creates++; return new(main, coordinator, () => { }) { ShowActivated = false, ShowInTaskbar = false }; });
            try
            {
                companion.Open(); Check(creates == 0);
                Check(companion.SetEnabled(true) && companion.Window is null);
                companion.Open(); var first = companion.Window; companion.Open(); Check(creates == 1 && ReferenceEquals(first, companion.Window));
                Check(first!.Width == 880 && first.Height == 650 && first.MinWidth == 640 && first.MinHeight == 480);
                first.Close(); Check(companion.Window is null && preferences.ShowDiagnostics && main.Model.Snapshot is not null);
                companion.Open(); Check(creates == 2);
                preferences.BeforeReplace = () => throw new IOException("Injected save failure");
                Check(!companion.SetEnabled(false) && companion.Window is not null && preferences.ShowDiagnostics);
                preferences.BeforeReplace = null; Check(companion.SetEnabled(false) && companion.Window is null);
                Check(companion.SetEnabled(true) && companion.Window is null); companion.Open(); companion.Dispose(); Check(companion.Window is null);
                var reloaded = new UiPreferences(directory); reloaded.Load(); Check(reloaded.ShowDiagnostics);
                using var restarted = new DiagnosticsCompanion(reloaded, () => throw new InvalidOperationException("must not reopen"));
                Check(restarted.Window is null);
            }
            finally { companion.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        test("Diagnostics uses shared authority and command coordination and detaches on close", () =>
        {
            var coordinator = new CommandCoordinator(); var pending = new TaskCompletionSource<HostCommandResult>();
            var calls = 0;
            var main = new MainWindow(command => coordinator.ExecuteAsync(command, _ => { calls++; return pending.Task; }), () => Task.CompletedTask, () => Task.CompletedTask);
            var seed = snapshot(); main.Apply(seed);
            var diagnostics = new DiagnosticsWindow(main, coordinator, () => { }) { ShowActivated = false, ShowInTaskbar = false };
            Check(ReferenceEquals(diagnostics.DataContext, main.Model));
            diagnostics.Show(); diagnostics.UpdateLayout();
            var retry = HomeShellInspection.Descendants((DependencyObject)diagnostics.Content).OfType<Button>()
                .Single(button => button.Content is TextBlock text && text.Text == "Retry save");
            Check(retry.IsEnabled == main.Model.CanRetrySave && !retry.IsEnabled);
            main.Apply(seed with { Sequence = 2, Persistence = PersistenceStatus.PendingCommit });
            Check(retry.IsEnabled == main.Model.CanRetrySave && retry.IsEnabled);
            main.Apply(seed with { Sequence = 3 });
            var request = main.ExecuteDiagnosticAsync(HostCommandKind.RetrySave);
            Check(coordinator.IsPending && calls == 1);
            Complete(main.ExecuteDiagnosticAsync(HostCommandKind.Pause)); Check(calls == 1);
            pending.SetResult(HostCommandResult.Success()); Complete(request);
            Check(coordinator.LastResult is { Succeeded: true } && coordinator.LastResult.Message.Length > 0);
            diagnostics.Close();
            main.Apply(seed with { Sequence = 4, State = seed.State! with { UsageMs = 120_000 } });
            Check(main.Model.Tray.Allowance.Contains(main.Model.HeroValue));
        });
        test("Diagnostics capture action is explicit in-memory and separate from accounting commands", () =>
        {
            var calls = 0;
            var main = new MainWindow(_ => { calls++; return Task.FromResult(HostCommandResult.Success()); }, () => Task.CompletedTask, () => Task.CompletedTask);
            main.Apply(snapshot());
            var capture = new ReminderAuthorizationCapture("26300.9711");
            var diagnostics = new DiagnosticsWindow(main, new CommandCoordinator(), () => { }, () => capture.Snapshot, capture.Arm)
            { ShowActivated = false, ShowInTaskbar = false };
            diagnostics.Show(); diagnostics.UpdateLayout();
            var button = HomeShellInspection.Descendants((DependencyObject)diagnostics.Content).OfType<Button>()
                .Single(x => x.Content is string text && text == "Capture next reminder check");
            Check(button.IsEnabled && capture.Snapshot.State == AuthorizationCaptureState.Idle);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!button.IsEnabled && capture.Snapshot.State == AuthorizationCaptureState.Armed && calls == 0);
            var result = HomeShellInspection.Descendants((DependencyObject)diagnostics.Content).OfType<TextBox>()
                .Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Reminder authorization capture result");
            Check(result.IsReadOnly && result.Text.Contains("Armed", StringComparison.Ordinal));
            diagnostics.Close();
        });
        test("temporary Diagnostics opening never changes its saved preference or arms capture", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "offramp-transient-diagnostics-" + Guid.NewGuid().ToString("N"));
            var preferences = new UiPreferences(directory); preferences.Load();
            var capture = new ReminderAuthorizationCapture("26300.9711");
            var main = new MainWindow(_ => throw new InvalidOperationException("No host command expected"), () => Task.CompletedTask, () => Task.CompletedTask);
            main.Apply(snapshot());
            using var companion = new DiagnosticsCompanion(preferences,
                () => new DiagnosticsWindow(main, new CommandCoordinator(), () => { }, () => capture.Snapshot, capture.Arm) { ShowActivated = false, ShowInTaskbar = false });
            companion.Open(); Check(companion.Window is null);
            companion.OpenTransient();
            Check(companion.Window is not null && !preferences.ShowDiagnostics && !Directory.Exists(directory));
            Check(capture.Snapshot.State == AuthorizationCaptureState.Idle);
        });
    }
}
