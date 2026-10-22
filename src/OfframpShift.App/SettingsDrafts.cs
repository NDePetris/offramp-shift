using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

internal enum UnsavedChoice { Save, Discard, KeepEditing }
internal enum SettingsGroup { Durations, ReminderStyle, Repeat, Appearance, Switching }

public partial class MainWindow
{
    private sealed record DraftBaseline(ExpectedSettingsValues Expected, string[] Fields);
    private readonly Dictionary<SettingsGroup, DraftBaseline> drafts = new();
    private readonly Dictionary<SettingsGroup, string> draftErrors = new();
    private SettingsGroup? savingGroup;
    private bool protectingDrafts;
    internal Func<UnsavedChoice>? ChooseUnsaved { get; set; }
    internal bool HasUnsavedChanges => drafts.Keys.Any(IsDirty);
    internal bool SavePending => savingGroup is not null;

    private static HostCommandKind Kind(SettingsGroup group) => group switch
    {
        SettingsGroup.Durations => HostCommandKind.ChangeDurations,
        SettingsGroup.ReminderStyle => HostCommandKind.ChangeBreakReminders,
        SettingsGroup.Repeat => HostCommandKind.ChangeInteractiveRepeat,
        SettingsGroup.Appearance => HostCommandKind.ChangeAppearance,
        _ => HostCommandKind.ChangeSwitchingConsent
    };
    private string[] Fields(SettingsGroup group) => group switch
    {
        SettingsGroup.Durations => [AllowanceInput.Text, BreakInput.Text, IdleInput.Text],
        SettingsGroup.ReminderStyle => [RemindersInput.SelectedIndex.ToString(CultureInfo.InvariantCulture)],
        SettingsGroup.Repeat => [RepeatInput.Text],
        SettingsGroup.Appearance => [ThemeInput.SelectedIndex.ToString(CultureInfo.InvariantCulture), (CloseInput.IsChecked == true).ToString()],
        _ => [(SwitchingInput.IsChecked == true).ToString()]
    };
    private bool IsDirty(SettingsGroup group) => drafts.TryGetValue(group, out var baseline) && !Fields(group).SequenceEqual(baseline.Fields);
    private void ClearValidation(SettingsGroup group)
    {
        Control[] fields = group switch
        {
            SettingsGroup.Durations => [AllowanceInput, BreakInput, IdleInput],
            SettingsGroup.Repeat => [RepeatInput],
            SettingsGroup.ReminderStyle => [RemindersInput],
            _ => []
        };
        foreach (var field in fields) System.Windows.Automation.AutomationProperties.SetHelpText(field, "");
    }
    private IEnumerable<SettingsGroup> PageGroups => CurrentPage switch
    {
        "Monitoring" => [SettingsGroup.Durations],
        "Reminders" => [SettingsGroup.ReminderStyle, SettingsGroup.Repeat],
        "Appearance" => [SettingsGroup.Appearance],
        "Advanced" => [SettingsGroup.Switching],
        _ => []
    };
    private void RefreshDrafts(HostSnapshot snapshot)
    {
        foreach (var group in Enum.GetValues<SettingsGroup>())
        {
            if (savingGroup == group || IsDirty(group)) continue;
            LoadDraft(group, snapshot);
        }
        if (snapshot.Configuration is { } configuration)
        {
            if (PageGroups.Any(group => IsDirty(group) && !drafts[group].Expected.Matches(Kind(group), snapshot.Settings, configuration)))
                SettingsResult.Text = "Saved values changed while you were editing. Your draft is retained. Cancel to load the confirmed values.";
        }
        if (PageGroups.Any(IsDirty) && !Model.CommandsIdle) return;
        var error = PageGroups.Select(group => draftErrors.GetValueOrDefault(group)).FirstOrDefault(value => value is not null);
        if (error is not null) SettingsResult.Text = error;
        SwitchingCapability.Text = (DesktopFeasibility.SwitchCompatibility.Is26300Family(snapshot.Settings.Build)
            ? "Consent applies to Windows build family 26300 on x64. " : "Consent applies to exact build " + snapshot.Settings.Build + ". ") +
            (DesktopFeasibility.SwitchCompatibility.Application(snapshot.Settings.Build, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())
            ? "Each explicit attempt is independently validated." : "Switching is unavailable on this build or architecture.");
    }
    private void LoadDraft(SettingsGroup group, HostSnapshot snapshot)
    {
        static string Duration(long value, long divisor) => ((decimal)value / divisor).ToString(CultureInfo.CurrentCulture);
        switch (group)
        {
            case SettingsGroup.Durations:
                if (snapshot.Configuration is not { } config) return;
                AllowanceInput.Text = Duration(config.AllowanceMs, 60_000);
                BreakInput.Text = Duration(config.RequiredBreakMs, 60_000);
                IdleInput.Text = Duration(config.IdleThresholdMs, 1_000); break;
            case SettingsGroup.Repeat:
                if (snapshot.Configuration is null) return;
                RepeatInput.Text = Duration(snapshot.Configuration.InteractiveRepeatNudgeMs, 60_000); break;
            case SettingsGroup.Appearance:
                ThemeInput.SelectedItem = snapshot.Settings.Theme;
                CloseInput.IsChecked = snapshot.Settings.CloseToTray ?? false; break;
            case SettingsGroup.ReminderStyle: RemindersInput.SelectedIndex = (int)snapshot.Settings.ReminderStyle; break;
            case SettingsGroup.Switching: SwitchingInput.IsChecked = snapshot.Settings.SwitchingConsent; break;
        }
        drafts[group] = new(ExpectedSettingsValues.From(snapshot.Settings, snapshot.Configuration), Fields(group));
    }

    private bool CanSave(SettingsGroup group) => Model.CommandsIdle && drafts.ContainsKey(group) && (group switch
    {
        SettingsGroup.Durations or SettingsGroup.Repeat => Model.CanEdit,
        _ => Model.CanEditReminders
    });

    internal async Task<bool> SaveDraftAsync(SettingsGroup group)
    {
        if (savingGroup is not null || !CanSave(group))
        { SettingsResult.Text = "Save is unavailable while the host or storage is unavailable, or another command is pending. Keep editing or explicitly discard your changes."; return false; }
        HostCommand command;
        switch (group)
        {
            case SettingsGroup.Durations:
                if (!ParseDuration(AllowanceInput.Text, 60_000, false, out var allowance)) return Invalid(group, AllowanceInput, "Enter a positive Personal allowance with millisecond precision or less.");
                if (!ParseDuration(BreakInput.Text, 60_000, false, out var requiredBreak)) return Invalid(group, BreakInput, "Enter a positive qualifying break with millisecond precision or less.");
                if (!ParseDuration(IdleInput.Text, 1_000, true, out var idle)) return Invalid(group, IdleInput, "Enter a nonnegative inactivity threshold with millisecond precision or less.");
                command = new(Kind(group)) { AllowanceMs = allowance, RequiredBreakMs = requiredBreak, IdleThresholdMs = idle }; break;
            case SettingsGroup.Repeat:
                if (!int.TryParse(RepeatInput.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var minutes) || minutes is < 1 or > 60)
                    return Invalid(group, RepeatInput, "Enter a whole number of minutes from 1 to 60.");
                command = new(Kind(group)) { DurationMs = minutes * 60_000L }; break;
            case SettingsGroup.Appearance:
                command = new(Kind(group)) { Theme = ThemeInput.SelectedItem is AppTheme theme ? theme : AppTheme.System, CloseToTray = CloseInput.IsChecked == true }; break;
            case SettingsGroup.ReminderStyle:
                if (RemindersInput.SelectedIndex is < 0 or > 2) return Invalid(group, RemindersInput, "Choose a reminder style.");
                command = new(Kind(group)) { ReminderStyle = (ReminderStyle)RemindersInput.SelectedIndex }; break;
            default: command = new(Kind(group)) { Confirmed = SwitchingInput.IsChecked == true }; break;
        }
        ClearValidation(group);
        savingGroup = group;
        draftErrors.Remove(group);
        try
        {
            var submitted = Fields(group);
            var result = await Run(command with { Expected = drafts[group].Expected });
            if (!result.Succeeded)
            { draftErrors[group] = result.Message; SettingsResult.Text = result.Message; return false; }
            // Completion is authoritative. Do not use a publication that may predate
            // this command to declare a save successful or erase a later edit.
            var expected = drafts[group].Expected;
            expected = group switch
            {
                SettingsGroup.Durations => expected with { AllowanceMs = command.AllowanceMs, RequiredBreakMs = command.RequiredBreakMs, IdleThresholdMs = command.IdleThresholdMs },
                SettingsGroup.Repeat => expected with { RepeatMs = command.DurationMs },
                SettingsGroup.Appearance => expected with { Theme = command.Theme, CloseToTray = command.CloseToTray },
                SettingsGroup.ReminderStyle => expected with { ReminderStyle = command.ReminderStyle },
                _ => expected with { SwitchingConsent = command.Confirmed, SwitchingBuild = command.Confirmed && Model.Snapshot is { } s && !DesktopFeasibility.SwitchCompatibility.Is26300Family(s.Settings.Build) ? s.Settings.Build : "", SwitchingFamily = command.Confirmed && Model.Snapshot is { } f && DesktopFeasibility.SwitchCompatibility.Is26300Family(f.Settings.Build) ? "26300" : "" }
            };
            drafts[group] = new(expected, submitted);
            ReportAction("Changes saved."); return true;
        }
        finally { savingGroup = null; }
    }
    private bool Invalid(SettingsGroup group, Control control, string message)
    {
        draftErrors[group] = message;
        ReportAction(message);
        System.Windows.Automation.AutomationProperties.SetHelpText(control, message);
        control.BringIntoView(); control.Focus(); return false;
    }
    internal async Task<bool> ProtectDraftsAsync(bool all = true)
    {
        if (protectingDrafts || savingGroup is not null)
        { ReportAction("Saving settings · Wait for confirmation before leaving or closing."); return false; }
        var dirty = (all ? drafts.Keys : PageGroups).Where(IsDirty).ToArray();
        if (dirty.Length == 0) return true;
        protectingDrafts = true;
        try
        {
            var choice = ChooseUnsaved?.Invoke() ?? UnsavedChangesDialog.Choose(this, dirty.All(CanSave));
            if (choice == UnsavedChoice.KeepEditing) return false;
            if (choice == UnsavedChoice.Discard)
            {
                foreach (var group in dirty) Discard(group);
                return true;
            }
            // Deliberately separate commands, never an atomic Save All transaction.
            foreach (var group in dirty) if (!await SaveDraftAsync(group)) return false;
            return !dirty.Any(IsDirty);
        }
        finally { protectingDrafts = false; }
    }
    private void Discard(SettingsGroup group)
    {
        ClearValidation(group);
        draftErrors.Remove(group);
        if (Model.Snapshot is { } snapshot && (group is SettingsGroup.Durations or SettingsGroup.Repeat
            ? snapshot.Configuration is not null : snapshot.SettingsStatus is SettingsStatus.Ready or SettingsStatus.Missing)) LoadDraft(group, snapshot);
        else if (drafts.TryGetValue(group, out var baseline))
        {
            var fields = baseline.Fields;
            switch (group)
            {
                case SettingsGroup.Durations: AllowanceInput.Text = fields[0]; BreakInput.Text = fields[1]; IdleInput.Text = fields[2]; break;
                case SettingsGroup.Repeat: RepeatInput.Text = fields[0]; break;
                case SettingsGroup.ReminderStyle: RemindersInput.SelectedIndex = int.Parse(fields[0], CultureInfo.InvariantCulture); break;
                case SettingsGroup.Appearance: ThemeInput.SelectedIndex = int.Parse(fields[0], CultureInfo.InvariantCulture); CloseInput.IsChecked = bool.Parse(fields[1]); break;
                case SettingsGroup.Switching: SwitchingInput.IsChecked = bool.Parse(fields[0]); break;
            }
        }
    }
    private void CancelDraft(object sender, RoutedEventArgs e)
    {
        if (savingGroup is not null) return;
        foreach (var group in PageGroups) Discard(group);
        SettingsResult.Text = ""; NavigateBack();
    }
}

internal static class UnsavedChangesDialog
{
    internal static UnsavedChoice Choose(Window owner, bool canSave)
    {
        var choice = UnsavedChoice.KeepEditing;
        var dialog = new Window
        {
            Title = "Unsaved changes",
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            MaxWidth = 440
        };
        var panel = new StackPanel { Margin = new Thickness(20), MaxWidth = 380 };
        panel.Children.Add(new TextBlock { Text = "Save your settings changes before leaving?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Each changed group saves separately. If a save fails, confirmed groups stay saved and the remaining drafts stay open.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        if (owner is MainWindow main && main.Model.Snapshot?.Persistence == OfframpShift.Persistence.PersistenceStatus.PendingCommit)
            panel.Children.Add(new TextBlock { Text = "Discard removes only the editor draft. A checkpoint is already pending; Retry save still retries that exact checkpoint.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        if (!canSave) panel.Children.Add(new TextBlock { Text = "Save is unavailable while the host, storage or another command prevents it. You can keep editing or discard the draft.", TextWrapping = TextWrapping.Wrap });
        foreach (var (label, value) in new[] { ("Save changes", UnsavedChoice.Save), ("Discard changes", UnsavedChoice.Discard), ("Keep editing", UnsavedChoice.KeepEditing) })
        {
            var button = new Button { Content = label, MinHeight = 36, Margin = new Thickness(0, 4, 0, 0), IsEnabled = value != UnsavedChoice.Save || canSave, IsCancel = value == UnsavedChoice.KeepEditing };
            button.Click += (_, _) => { choice = value; dialog.Close(); }; panel.Children.Add(button);
        }
        dialog.Content = panel; dialog.ShowDialog(); return choice;
    }
}
