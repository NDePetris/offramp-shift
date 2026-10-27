using System.Windows;
using System.Windows.Threading;
using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;

namespace OfframpShift.App;

public partial class ShiftApplication : Application
{
    private readonly SingleInstance instance = null!;
    private MonitoringController? controller;
    private MainWindow? window;
    private TrayService? tray;
    private DispatcherTimer? timer;
    private bool exiting, closing;
    private int actionsInFlight;
    private AppTheme? theme;
    private NudgePresenter? nudges;
    private UiPreferences? uiPreferences;
    private DiagnosticsCompanion? diagnostics;
    private readonly CommandCoordinator commands = new();
    public ShiftApplication() { InitializeComponent(); }
    internal ShiftApplication(SingleInstance instance) : this() { this.instance = instance; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        controller = WindowsApplication.Start();
        window = new(Execute, ExitAsync, CloseAsync); MainWindow = window;
        uiPreferences = new(SqliteCheckpointStore.DefaultDirectory); uiPreferences.Load();
        diagnostics = new(uiPreferences, () => new DiagnosticsWindow(window, commands, () => Open(false)));
        window.ShowDiagnosticsPreference(uiPreferences.ShowDiagnostics, uiPreferences.Error);
        window.SetDiagnosticsEnabled = enabled =>
        {
            var saved = diagnostics.SetEnabled(enabled);
            window.ShowDiagnosticsPreference(uiPreferences.ShowDiagnostics, uiPreferences.Error);
            return saved;
        };
        window.OpenDiagnosticsAction = diagnostics.Open;
        commands.Changed += () =>
        {
            window.Model.SetCommandPending(commands.IsPending);
            window.ReportAction(commands.Message);
            if (window.Model.Snapshot is { } current) tray?.Apply(current, window.Model);
        };
        nudges = new(command => command.Kind is HostCommandKind.BeginNudgePresentation or HostCommandKind.CompleteNudgePresentation ? controller.ExecuteAsync(command) : Execute(command),
            () => controller.Completion.IsCompleted || exiting ? null : controller.Latest,
            detail => { window.ReportAction(detail); tray?.ReportNudgeFailure(detail); }, new NudgePlatform(),
            (handle, cancellation) => controller.SwitchToWorkAsync(handle, cancellationToken: cancellation), () => Open(false));
        try { tray = new(() => Open(false), () => Open(true), Execute, ExitAsync); }
        catch { MessageBox.Show("The tray icon could not be created. Keep this window open; closing will exit.", "Offramp Shift"); }
        nudges.OpenStateChanged += () => tray?.SetReminderOpen(nudges.HasOpenReminder);
        RefreshBrand();
        instance.Listen(() => Dispatcher.BeginInvoke(() => Open(false)));
        timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Refresh(); timer.Start();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        window.Show();
        SessionEnding += (_, _) => controller.StopAsync();
    }
    private void DisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => nudges?.Close());
    private void Open(bool settings)
    {
        if (window is null || exiting) return;
        window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (settings) window.OpenSettings();
        window.Activate();
    }
    private void Refresh()
    {
        if (controller is null || window is null) return;
        RefreshBrand();
        commands.RefreshWaiting();
        if (controller.Latest is not { } value)
        {
            if (controller.Completion.IsCompleted)
            {
                window.Model.MarkUnavailable("Application startup failed. Accounting is unavailable; exit and restart after checking local storage.");
                tray?.MarkUnavailable();
                nudges?.Close();
            }
            return;
        }
        window.Apply(value, controller.Completion.IsCompleted); tray?.Apply(value, window.Model);
        nudges?.Refresh(controller.Completion.IsCompleted || exiting);
        window.NudgeStatus.Visibility = value.Settings.ReminderStyle == ReminderStyle.InteractiveWindow ? Visibility.Visible : Visibility.Collapsed;
        window.NudgeStatus.Text = "Last reminder: " + value.ReminderStatus + " " + nudges?.DiagnosticStatus;
        diagnostics?.Window?.Refresh();
        if (controller.TryTakeReminder(out var reminder) && actionsInFlight == 0 && Environment.TickCount64 - value.PublishedTick is >= 0 and <= 2_000)
        {
            if (reminder?.Style == ReminderStyle.InteractiveWindow) _ = nudges?.PresentAsync(reminder);
            else if (value.Settings.ReminderStyle == ReminderStyle.WindowsNotification) tray?.RequestReminder();
        }
        if (theme != value.Settings.Theme)
        {
            theme = value.Settings.Theme;
            ApplyTheme(theme.Value);
            RefreshBrand();
        }
        if (controller.Completion.IsCompleted && !exiting)
            window.ActionResult.Text = "The monitoring owner stopped or cleanup failed. Accounting is stopped. Exit and restart the app.";
    }
    private void RefreshBrand()
    {
        if (window is null) return;
        var dark = BrandAssets.IsDark(window.TryFindResource("WindowBackground") as System.Windows.Media.Brush);
        window.ApplyBrand(dark, SystemParameters.HighContrast); tray?.ApplyBrand(dark);
    }
    // The framework Fluent theme selector remains evaluation-marked in .NET 10.
    // Keep the opt-in local; all other warnings remain errors.
#pragma warning disable WPF0001
    internal void ApplyTheme(AppTheme value) => ThemeMode = value switch
    { AppTheme.Light => System.Windows.ThemeMode.Light, AppTheme.Dark => System.Windows.ThemeMode.Dark, _ => System.Windows.ThemeMode.System };
#pragma warning restore WPF0001
    private async Task<HostCommandResult> Execute(HostCommand command)
    {
        // Keep the nudge's durable deferral transaction and reporting path intact.
        if (command.Kind == HostCommandKind.DeferInteractive) return await ExecuteHost(command);
        if (commands.IsPending) return HostCommandResult.Failure("An action is already awaiting confirmation.");
        var result = await commands.ExecuteAsync(command, ExecuteHost);
        tray?.ReportActionResult(command, result);
        return result;
    }
    private async Task<HostCommandResult> ExecuteHost(HostCommand command)
    {
        if (controller is null || exiting) return HostCommandResult.Failure("The app is stopping.");
        actionsInFlight++;
        try
        {
            var result = await controller.ExecuteAsync(command);
            // NudgePresenter reports actionable deferral failures once, after
            // distinguishing a failed save from automatic context closure.
            if (command.Kind == HostCommandKind.DeferInteractive && result.Succeeded)
                window?.ReportAction(result.Message.Length == 0 ? "Extra Personal time saved." : result.Message);
            Refresh(); return result;
        }
        finally { actionsInFlight--; }
    }
    private async Task CloseAsync()
    {
        if (closing || exiting || window is null) return;
        closing = true;
        try
        {
            if (!await window.ProtectDraftsAsync()) return;
            if (tray is null) { await ExitAsync(); return; }
            var choice = controller?.Latest?.Settings.CloseToTray;
            bool? selected = null;
            if (choice is null)
            {
                var answer = MessageBox.Show(window, "Keep monitoring in the system tray when this window closes? Yes keeps the app running; No exits. You can change this in Settings. Tray Exit always stops the app.",
                    "Close Offramp Shift", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                if (answer == MessageBoxResult.Cancel) return;
                selected = answer == MessageBoxResult.Yes;
            }
            choice = await ClosePreference.ResolveAsync(choice, selected, Execute, detail =>
            {
                window.ReportAction(detail);
                MessageBox.Show(window, detail, "Close preference not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            if (choice is null) return;
            if (choice == true) window.Hide(); else await ExitAsync();
        }
        finally { closing = false; }
    }
    private async Task ExitAsync()
    {
        if (exiting || controller is null || window is null) return;
        if (!await window.ProtectDraftsAsync()) return;
        if (controller.Latest?.Persistence == PersistenceStatus.PendingCommit)
        {
            var answer = MessageBox.Show(window, "A checkpoint is pending. Yes retries the exact save; No exits and may lose the pending change; Cancel keeps the app open.",
                "Pending save", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes) { await Execute(new(HostCommandKind.RetrySave)); return; }
        }
        exiting = true; diagnostics?.Dispose(); nudges?.Close(); window.ActionResult.Text = "Stopping monitoring and disposing providers…";
        var stopped = controller.StopAsync();
        if (await Task.WhenAny(stopped, Task.Delay(12_000)) != stopped)
        {
            window.Show();
            MessageBox.Show(window, "Provider cleanup has not completed. Accounting has stopped; the app is still waiting for an in-flight native operation. No successful shutdown is claimed.", "Offramp Shift");
        }
        try { await stopped; }
        catch { MessageBox.Show(window, "The monitoring owner stopped with an error. Accounting stopped, but resource cleanup could not be confirmed.", "Offramp Shift"); }
        timer?.Stop(); tray?.Dispose(); Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        diagnostics?.Dispose();
        nudges?.Close(); controller?.StopAsync(); timer?.Stop(); tray?.Dispose();
        base.OnExit(e);
    }
}
