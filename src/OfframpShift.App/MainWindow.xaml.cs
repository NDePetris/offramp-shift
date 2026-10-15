using System.ComponentModel;
using System.Globalization;
using System.Windows;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;

namespace OfframpShift.App;

public partial class MainWindow : Window
{
    public DashboardModel Model { get; } = new();
    private readonly Func<HostCommand, Task<string>> execute;
    private readonly Func<Task> exit;
    private readonly Func<Task> close;
    private MonitoringConfiguration? displayedConfiguration;
    private WindowsSettings? displayedSettings;
    private bool busy;

    internal MainWindow(Func<HostCommand, Task<string>> execute, Func<Task> exit, Func<Task> close)
    {
        InitializeComponent(); DataContext = Model;
        this.execute = execute; this.exit = exit; this.close = close;
        ThemeInput.ItemsSource = Enum.GetValues<AppTheme>();
        Closing += OnClosing;
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
        }
        if (snapshot.Settings != displayedSettings)
        { displayedSettings = snapshot.Settings; ThemeInput.SelectedItem = snapshot.Settings.Theme; CloseInput.IsChecked = snapshot.Settings.CloseToTray ?? false; }
    }

    private async Task Run(HostCommand command)
    {
        if (busy) return;
        busy = true;
        try
        {
            ActionResult.Text = SettingsResult.Text = "Applying…";
            var result = await execute(command);
            ActionResult.Text = SettingsResult.Text = result.Length == 0 ? "Saved action completed." : result;
        }
        finally { busy = false; }
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
