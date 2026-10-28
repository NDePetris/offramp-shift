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
    internal Func<bool, bool>? SetDiagnosticsEnabled { get; set; }
    internal Action? OpenDiagnosticsAction { get; set; }
    internal Func<Task>? RequestPause { get; set; }
    internal void ShowDiagnosticsPreference(bool enabled, string error)
    { DiagnosticsInput.IsChecked = enabled; OpenDiagnosticsButton.IsEnabled = enabled; DiagnosticsPreferenceResult.Text = error; }
    private void ChangeDiagnostics(object sender, RoutedEventArgs e) => SetDiagnosticsEnabled?.Invoke(DiagnosticsInput.IsChecked == true);
    private void OpenDiagnostics(object sender, RoutedEventArgs e) => OpenDiagnosticsAction?.Invoke();
    internal async Task ExecuteDiagnosticAsync(HostCommandKind kind)
    {
        if (kind == HostCommandKind.Recover) Recover(this, new());
        else if (kind == HostCommandKind.RepairSettings) RepairSettings(this, new());
        else await Run(new(kind));
    }
    internal const string ResetUsageConfirmation = "Reset accumulated usage and clear current break progress, including any completed break? " +
        "This cannot be undone. Your monitoring settings and Pause state will stay the same.";
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
        QuietRoadPalette.Apply(this, dark, highContrast);
        HeroRoad.ApplyTheme(dark, highContrast);
        Icon = BrandAssets.WindowIcon(dark);
    }

    internal void Apply(HostSnapshot snapshot, bool ownerStopped = false)
    {
        if (ownerStopped) { Model.MarkUnavailable("The monitoring owner stopped. Exit and restart the app."); return; }
        var wasPaused = Model.Snapshot?.State?.Paused == true;
        var previousPauseStatus = Model.PauseStatus;
        Model.Apply(snapshot, Environment.TickCount64);
        if (!ReferenceEquals(Model.Snapshot, snapshot)) return;
        if (Model.PauseStatus != previousPauseStatus && Model.PauseStatus.Length > 0)
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(PauseStatusText)?
                .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        if (wasPaused && snapshot.State?.Paused == false)
            ReportAction("Pause ended and saved. Monitoring still requires valid setup and fresh evidence.");
        RefreshDrafts(snapshot);
    }

    private async Task<HostCommandResult> Run(HostCommand command)
    {
        if (!Model.CommandsIdle) return HostCommandResult.Failure("An action is already pending.");
        Model.SetCommandPending(true);
        try
        {
            ReportAction(CommandCoordinator.PendingText(command.Kind));
            var result = await execute(command);
            ReportAction(result.Message.Length == 0 ? result.Succeeded ? "Action confirmed." : "The action did not complete." : result.Message);
            return result;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var failure = HostCommandResult.Failure("The action could not be confirmed. Your draft is retained; review monitoring and storage before trying again.");
            ReportAction(failure.Message); return failure;
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
        originatingRow = ((System.Windows.Controls.Panel)SettingsOverview.Content).Children.OfType<System.Windows.Controls.Button>().First(button => (string)button.Tag == "Desktop");
        Navigate("Desktop");
        SetupHeading.BringIntoView();
        if (Model.CanConsent) ConsentButton.Focus();
        else if (Model.CanStart) SetupStartButton.Focus();
        else if (Model.CanAssign) PersonalButton.Focus();
        else BackButton.Focus();
    }
    internal async void OpenSettings() { if (await ProtectDraftsAsync()) Navigate("Settings"); }
    private void OpenSettings(object sender, RoutedEventArgs e) => OpenSettings();
    private void OpenDetail(object sender, RoutedEventArgs e)
    {
        originatingRow = (System.Windows.Controls.Button)sender;
        Navigate((string)originatingRow.Tag);
    }
    private void GoBack(object sender, RoutedEventArgs e) => NavigateBack();
    internal async void NavigateBack()
    {
        if (CurrentPage == "Home" || !await ProtectDraftsAsync(false)) return;
        var home = CurrentPage == "Settings";
        Navigate(home ? "Home" : "Settings");
        if (home) SettingsButton.Focus(); else originatingRow?.Focus();
    }
    private void Navigate(string page)
    {
        SettingsResult.Text = "";
        CurrentPage = page;
        HomeHeader.Visibility = HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = page == "Home" ? Visibility.Collapsed : Visibility.Visible;
        SettingsOverview.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        MonitoringActions.Visibility = page == "Monitoring" ? Visibility.Visible : Visibility.Collapsed;
        SettingsDetail.Visibility = page is "Home" or "Settings" ? Visibility.Collapsed : Visibility.Visible;
        foreach (var section in new[] { DesktopSection, MonitoringSection, RemindersSection, AppearanceSection, AdvancedSection })
            section.Visibility = section.Name == page + "Section" ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(BackButton, page == "Settings" ? "Back to Home" : "Back to Settings");
        SettingsDetail.ScrollToTop();
        if (Model.Snapshot is { } snapshot) RefreshDrafts(snapshot);
        if (page != "Home")
        {
            System.Windows.Controls.Control target = page switch
            {
                "Monitoring" => AllowanceInput,
                "Reminders" => RemindersInput,
                "Appearance" => ThemeInput,
                "Advanced" => DiagnosticsInput,
                _ => BackButton
            };
            target.Focus();
        }
    }
    private async void ToggleMonitoring(object sender, RoutedEventArgs e)
    {
        if (Model.Snapshot?.State?.Paused == true) await Run(new(HostCommandKind.Resume));
        else if (RequestPause is not null) await RequestPause();
    }
    private async void ChangePause(object sender, RoutedEventArgs e) { if (RequestPause is not null) await RequestPause(); }
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
    private async void SaveDurations(object sender, RoutedEventArgs e) => await SaveDraftAsync(SettingsGroup.Durations);
    private async void SaveAppearance(object sender, RoutedEventArgs e) => await SaveDraftAsync(SettingsGroup.Appearance);
    private async void SaveReminders(object sender, RoutedEventArgs e) => await SaveDraftAsync(SettingsGroup.ReminderStyle);
    private async void SaveSwitching(object sender, RoutedEventArgs e) => await SaveDraftAsync(SettingsGroup.Switching);
    private async void SaveRepeat(object sender, RoutedEventArgs e) => await SaveDraftAsync(SettingsGroup.Repeat);
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
