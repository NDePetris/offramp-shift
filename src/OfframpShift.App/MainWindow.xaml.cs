using System.ComponentModel;
using System.Globalization;
using System.Windows;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;

namespace OfframpShift.App;

public partial class MainWindow : Window
{
    public DashboardModel Model { get; } = new();
    private readonly Func<HostCommand, Task<HostCommandResult>> execute;
    private readonly Func<Task> exit;
    private readonly Func<Task> close;
    private readonly Func<bool> confirmUsageReset;
    internal const string ResetUsageConfirmation = "Reset accumulated usage and clear current break progress, including any completed break? " +
        "This cannot be undone. Your monitoring settings and Pause state will stay the same.";
    private MonitoringConfiguration? displayedConfiguration;
    private WindowsSettings? displayedSettings;
    internal string CurrentPage { get; private set; } = "Home";
    private System.Windows.Controls.Button? originatingRow;
    private (bool Dark, bool HighContrast)? artwork;

    internal MainWindow(Func<HostCommand, Task<HostCommandResult>> execute, Func<Task> exit, Func<Task> close, Func<bool>? confirmUsageReset = null)
    {
        InitializeComponent(); DataContext = Model;
        this.execute = execute; this.exit = exit; this.close = close;
        this.confirmUsageReset = confirmUsageReset ?? (() => MessageBox.Show(this, ResetUsageConfirmation, "Reset usage",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        ThemeInput.ItemsSource = Enum.GetValues<AppTheme>();
        RemindersInput.ItemsSource = new[] { "Off", "Windows notification", "Interactive window" };
        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (CurrentPage != "Home" && (e.Key == System.Windows.Input.Key.Left && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Alt))
            { e.Handled = true; NavigateBack(); }
        };
        ApplyBrand(BrandAssets.IsDark(Background), SystemParameters.HighContrast);
    }

    internal void ApplyBrand(bool dark, bool highContrast)
    {
        if (artwork == (dark, highContrast)) return;
        artwork = (dark, highContrast);
        System.Windows.Media.SolidColorBrush Brush(string color)
        {
            var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            brush.Freeze(); return brush;
        }
        Resources["ShiftCanvas"] = highContrast ? SystemColors.WindowBrush : Brush(dark ? "#1F2B28" : "#F6F3EC");
        Resources["ShiftText"] = highContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#F6F3EC" : "#1F2B28");
        Resources["ShiftSupporting"] = highContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#C8BCA7" : "#405F54");
        Resources["ShiftAccent"] = highContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#79AFC9" : "#2E5A4F");
        Resources["ShiftTrack"] = highContrast ? SystemColors.ControlBrush : Brush(dark ? "#50675D" : "#E3E3DC");
        Resources["ShiftDivider"] = highContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#50675D" : "#C8BCA7");
        // Context uses a native monitor glyph; canonical artwork stays in the
        // window/tray icons and the unchanged reminder surface.
        Icon = BrandAssets.WindowIcon(dark);
    }

    internal void Apply(HostSnapshot snapshot, bool ownerStopped = false)
    {
        if (ownerStopped) { Model.MarkUnavailable("The monitoring owner stopped. Exit and restart the app."); return; }
        Model.Apply(snapshot, Environment.TickCount64);
        if (!ReferenceEquals(Model.Snapshot, snapshot)) return;
        if (snapshot.Configuration is { } config && config != displayedConfiguration)
        {
            displayedConfiguration = config;
            AllowanceInput.Text = ((decimal)config.AllowanceMs / 60_000).ToString(CultureInfo.CurrentCulture);
            BreakInput.Text = ((decimal)config.RequiredBreakMs / 60_000).ToString(CultureInfo.CurrentCulture);
            IdleInput.Text = ((decimal)config.IdleThresholdMs / 1_000).ToString(CultureInfo.CurrentCulture);
            RepeatInput.Text = (config.InteractiveRepeatNudgeMs / 60_000).ToString(CultureInfo.CurrentCulture);
        }
        if (snapshot.Settings != displayedSettings)
        {
            displayedSettings = snapshot.Settings; ThemeInput.SelectedItem = snapshot.Settings.Theme;
            CloseInput.IsChecked = snapshot.Settings.CloseToTray ?? false;
            RemindersInput.SelectedIndex = (int)snapshot.Settings.ReminderStyle;
            SwitchingInput.IsChecked = snapshot.Settings.SwitchingConsent;
            SwitchingCapability.Text = (DesktopFeasibility.SwitchCompatibility.Is26300Family(snapshot.Settings.Build)
                ? "Consent applies to Windows build family 26300 on x64. "
                : "Consent applies to exact build " + snapshot.Settings.Build + ". ") +
                (DesktopFeasibility.SwitchCompatibility.Application(snapshot.Settings.Build, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())
                ? "Limited explicit experiments only; each click is independently validated."
                : "Application switching is unavailable on this build or architecture.");
        }
    }

    private async Task Run(HostCommand command)
    {
        if (!Model.CommandsIdle) return;
        Model.SetCommandPending(true);
        try
        {
            ReportAction(CommandCoordinator.PendingText(command.Kind));
            var result = await execute(command);
            ReportAction(result.Message.Length == 0 ? result.Succeeded ? "Action confirmed." : "The action did not complete." : result.Message);
        }
        finally { Model.SetCommandPending(false); }
    }
    internal void ReportAction(string message)
    {
        if (ActionResult.Text == message && SettingsResult.Text == message) return;
        ActionResult.Text = SettingsResult.Text = message;
        // WPF LiveSetting alone does not raise a UIA live-region event for TextBlock changes.
        var target = CurrentPage == "Home" ? ActionResult : SettingsResult;
        if (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(target) is { } peer)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private void OpenSetup(object sender, RoutedEventArgs e)
    {
        Navigate("Desktop");
        SetupHeading.BringIntoView();
        if (Model.CanConsent) ConsentButton.Focus();
        else if (Model.CanStart) SetupStartButton.Focus();
        else if (Model.CanAssign) PersonalButton.Focus();
        else BackButton.Focus();
    }
    internal void OpenSettings() => Navigate("Settings");
    private void OpenSettings(object sender, RoutedEventArgs e) => OpenSettings();
    private void OpenDetail(object sender, RoutedEventArgs e)
    {
        originatingRow = (System.Windows.Controls.Button)sender;
        Navigate((string)originatingRow.Tag);
    }
    private void GoBack(object sender, RoutedEventArgs e) => NavigateBack();
    internal void NavigateBack()
    {
        if (CurrentPage == "Home") return;
        var home = CurrentPage == "Settings";
        Navigate(home ? "Home" : "Settings");
        if (home) SettingsButton.Focus(); else originatingRow?.Focus();
    }
    private void Navigate(string page)
    {
        CurrentPage = page;
        HomeHeader.Visibility = HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = page == "Home" ? Visibility.Collapsed : Visibility.Visible;
        SettingsOverview.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        SettingsDetail.Visibility = page is "Home" or "Settings" ? Visibility.Collapsed : Visibility.Visible;
        foreach (var section in new[] { DesktopSection, MonitoringSection, RemindersSection, AppearanceSection, AdvancedSection })
            section.Visibility = section.Name == page + "Section" ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(BackButton, page == "Settings" ? "Back to Home" : "Back to Settings");
        SettingsDetail.ScrollToTop();
        if (page != "Home") BackButton.Focus();
    }
    private async void ToggleMonitoring(object sender, RoutedEventArgs e) => await Run(new(Model.Snapshot?.State?.Paused == true ? HostCommandKind.Resume : HostCommandKind.Pause));
    private async void Start(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.Start));
    private async void Pause(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.Pause));
    private async void Resume(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.Resume));
    private async void RetryMonitoring(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.RetryMonitoring));
    private async void RetrySave(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.RetrySave));
    private async void ReopenStorage(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.ReopenStorage));
    private async void Consent(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.Consent) { Confirmed = true });
    private async void AssignPersonal(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.AssignPersonal));
    private async void AssignWork(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.AssignWork));

    internal static bool ParseDuration(string text, long multiplier, bool zeroAllowed, out long value)
    {
        value = 0;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var number) || number < 0 || !zeroAllowed && number == 0) return false;
        try
        {
            var exact = number * multiplier;
            if (decimal.Truncate(exact) != exact) return false;
            value = checked((long)exact); return true;
        }
        catch (OverflowException) { return false; }
    }
    private async void SaveDurations(object sender, RoutedEventArgs e)
    {
        if (!ParseDuration(AllowanceInput.Text, 60_000, false, out var allowance) ||
            !ParseDuration(BreakInput.Text, 60_000, false, out var requiredBreak) ||
            !ParseDuration(IdleInput.Text, 1_000, true, out var idle))
        { SettingsResult.Text = "Enter positive allowance and break durations and a nonnegative reminder inactivity threshold, with millisecond precision or less."; return; }
        await Run(new(HostCommandKind.ChangeDurations) { AllowanceMs = allowance, RequiredBreakMs = requiredBreak, IdleThresholdMs = idle });
    }
    private async void SaveAppearance(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.ChangeAppearance)
    { Theme = ThemeInput.SelectedItem is AppTheme theme ? theme : AppTheme.System, CloseToTray = CloseInput.IsChecked == true });
    private async void SaveReminders(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.ChangeBreakReminders)
    { ReminderStyle = (ReminderStyle)RemindersInput.SelectedIndex });
    private async void SaveSwitching(object sender, RoutedEventArgs e) => await Run(new(HostCommandKind.ChangeSwitchingConsent)
    { Confirmed = SwitchingInput.IsChecked == true });
    private async void SaveRepeat(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RepeatInput.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var minutes) || minutes is < 1 or > 60)
        { SettingsResult.Text = "Enter a whole number of minutes from 1 to 60."; return; }
        await Run(new(HostCommandKind.ChangeInteractiveRepeat) { DurationMs = minutes * 60_000L });
    }
    private async void ResetUsage(object sender, RoutedEventArgs e)
    {
        if (!Model.CommandsIdle || !Model.CanResetUsage) return;
        if (confirmUsageReset()) await Run(new(HostCommandKind.ResetUsage) { Confirmed = true });
    }
    private async void Recover(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Reset unavailable accounting and start with a fresh allowance? Accumulated accounting may be lost. Original files will be preserved locally before replacement. This cannot be undone automatically.",
            "Recover accounting", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            await Run(new(HostCommandKind.Recover) { Confirmed = true });
    }
    private async void RepairSettings(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Preserve the unreadable Windows settings and replace them? You will need to opt in and assign desktops again. Accounting storage is retained.",
            "Repair Windows settings", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            await Run(new(HostCommandKind.RepairSettings) { Confirmed = true });
    }
    private async void Exit(object sender, RoutedEventArgs e) => await exit();
    private async void OnClosing(object? sender, CancelEventArgs e) { e.Cancel = true; await close(); }
}
