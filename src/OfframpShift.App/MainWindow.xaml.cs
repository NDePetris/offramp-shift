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
    private bool busy;
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
        ApplyBrand(BrandAssets.IsDark(Background), SystemParameters.HighContrast);
    }

    internal void ApplyBrand(bool dark, bool highContrast)
    {
        if (artwork == (dark, highContrast)) return;
        artwork = (dark, highContrast);
        FamilySymbol.Source = BrandAssets.Symbol(dark);
        // The decorative mark never overrides high-contrast system colors.
        // The adjacent native text heading remains the accessible app identity.
        FamilySymbol.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        Icon = BrandAssets.WindowIcon(dark);
    }

    internal void Apply(HostSnapshot snapshot, bool ownerStopped = false)
    {
        Model.Apply(snapshot, ownerStopped ? snapshot.PublishedTick + 2_001 : Environment.TickCount64);
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
        if (busy) return;
        busy = true;
        try
        {
            ActionResult.Text = SettingsResult.Text = "Applying…";
            var result = await execute(command);
            ReportAction(result.Message.Length == 0 ? "Saved action completed." : result.Message);
        }
        finally { busy = false; }
    }
    internal void ReportAction(string message)
    {
        if (ActionResult.Text == message && SettingsResult.Text == message) return;
        ActionResult.Text = SettingsResult.Text = message;
        // WPF LiveSetting alone does not raise a UIA live-region event for TextBlock changes.
        var target = Navigation.SelectedIndex == 1 ? SettingsResult : ActionResult;
        if (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(target) is { } peer)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private void OpenSetup(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedIndex = 1;
        SetupHeading.BringIntoView();
        if (Model.CanConsent) ConsentButton.Focus();
        else if (Model.CanStart) SetupStartButton.Focus();
        else if (Model.CanAssign) PersonalButton.Focus();
        else if (Navigation.SelectedItem is System.Windows.Controls.TabItem tab) tab.Focus();
    }
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
        { SettingsResult.Text = "Enter positive allowance and break durations and a nonnegative idle threshold, with millisecond precision or less."; return; }
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
        if (busy || !Model.CanResetUsage) return;
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
