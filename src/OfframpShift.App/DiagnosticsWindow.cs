using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

// A disposable view of the main DashboardModel, never an observation owner.
internal sealed class DiagnosticsWindow : Window
{
    private readonly MainWindow main;
    private readonly CommandCoordinator commands;
    private readonly Dictionary<string, TextBox> values = new();
    private readonly Grid columns = new();
    private readonly StackPanel actions = new();
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };

    internal DiagnosticsWindow(MainWindow main, CommandCoordinator commands, Action showHome)
    {
        this.main = main; this.commands = commands;
        Title = "Offramp Shift — Diagnostics"; Width = 880; Height = 650; MinWidth = 640; MinHeight = 480;
        FontFamily = main.FontFamily; FontSize = 14; DataContext = main.Model;
        Resources = main.Resources;
        SetResourceReference(BackgroundProperty, "ShiftCanvas"); SetResourceReference(ForegroundProperty, "ShiftText");
        Icon = main.Icon;
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var home = new Button { Content = "Show Home", MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Right };
        home.Click += (_, _) => showHome(); DockPanel.SetDock(home, Dock.Right); header.Children.Add(home);
        var headings = new StackPanel();
        headings.Children.Add(new TextBlock { Text = "Diagnostics", FontSize = 20, FontWeight = FontWeights.SemiBold });
        headings.Children.Add(new TextBlock { Text = "Runtime inspection · Same application runtime", TextWrapping = TextWrapping.Wrap });
        header.Children.Add(headings); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(StyleProperty, "ContentScroll");
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(240) });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto }); columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var detail = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        AddSection(detail, "Monitoring and observations", ["Monitoring intent", "Operational health", "Host snapshot age", "Context", "Desktop assignments"]);
        AddSection(detail, "Accounting and reminders", ["Recorded usage", "Estimated portion", "Qualifying break", "Reminder remainder", "Reminder presentation"]);
        AddSection(detail, "Storage and commands", ["Checkpoint storage", "Last command", "Command elapsed", "Last result"]);
        var technical = new Expander { Header = "Technical identifiers", Margin = new Thickness(0, 12, 0, 0) };
        var technicalBody = new StackPanel(); AddSection(technicalBody, "Runtime details", ["Observation consent", "Provider availability", "Recorded allowance", "Intervention", "Storage failure", "Windows settings", "Configuration", "Sequence / epoch", "Reminder generation", "Switching availability", "Engine state", "Desktop GUIDs"]);
        technical.Content = technicalBody; detail.Children.Add(technical); columns.Children.Add(detail);
        actions.Children.Add(new TextBlock { Text = "Available actions", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        actions.Children.Add(new TextBlock { Text = "Actions follow current host eligibility.", TextWrapping = TextWrapping.Wrap });
        foreach (var (label, kind, gate) in new[] {
            ("Retry monitoring", HostCommandKind.RetryMonitoring, nameof(DashboardModel.CanRetryMonitoring)),
            ("Retry save", HostCommandKind.RetrySave, nameof(DashboardModel.CanRetrySave)),
            ("Reopen storage", HostCommandKind.ReopenStorage, nameof(DashboardModel.CanReopen)),
            ("Recover accounting…", HostCommandKind.Recover, nameof(DashboardModel.CanRecover)),
            ("Repair Windows settings…", HostCommandKind.RepairSettings, nameof(DashboardModel.CanRepairSettings)) })
        {
            var button = new Button
            {
                MinHeight = 36,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }
            };
            button.SetBinding(IsEnabledProperty, new Binding(gate));
            button.Click += async (_, _) => await main.ExecuteDiagnosticAsync(kind); actions.Children.Add(button);
        }
        actions.Children.Add(new TextBlock { Text = "Recovery actions need review and may require confirmation.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        System.Windows.Automation.AutomationProperties.SetLiveSetting(feedback, System.Windows.Automation.AutomationLiveSetting.Polite);
        actions.Children.Add(feedback); Grid.SetColumn(actions, 1); columns.Children.Add(actions);
        scroll.Content = columns; root.Children.Add(scroll); Content = root;
        SizeChanged += Resize;
        main.Model.PropertyChanged += Updated; commands.Changed += Refresh;
        Closed += (_, _) => { main.Model.PropertyChanged -= Updated; commands.Changed -= Refresh; SizeChanged -= Resize; };
        Refresh();
    }
    private void Resize(object sender, SizeChangedEventArgs e)
    {
        var narrow = ActualWidth < 780;
        columns.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 240);
        Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetRow(actions, narrow ? 1 : 0);
        actions.Margin = new Thickness(0, narrow ? 20 : 0, 0, 0);
    }
    private void AddSection(Panel parent, string heading, string[] labels)
    {
        parent.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
        foreach (var label in labels)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(0.45, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(0.55, GridUnitType.Star) });
            var name = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 12, 0) };
            name.SetResourceReference(ForegroundProperty, "ShiftSupporting"); row.Children.Add(name);
            var value = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0, 4, 0, 0), Margin = new Thickness(0), MinWidth = 0, MinHeight = 0 };
            value.SetResourceReference(ForegroundProperty, "ShiftText");
            System.Windows.Automation.AutomationProperties.SetName(value, label);
            Grid.SetColumn(value, 1); row.Children.Add(value); values[label] = value; parent.Children.Add(row);
        }
    }
    private void Updated(object? sender, PropertyChangedEventArgs e) => Refresh();
    internal void Refresh()
    {
        Icon = main.Icon;
        var model = main.Model; var snapshot = model.Snapshot; var recorded = model.Recorded;
        void Set(string label, object? value)
        {
            var text = value?.ToString() ?? "Not exposed by this runtime";
            if (values[label].Text != text) values[label].Text = text;
        }
        Set("Monitoring intent", snapshot is null ? null : snapshot.Settings.MonitoringEnabled ? "Enabled" : "Disabled");
        Set("Operational health", model.Health);
        values["Operational health"].ToolTip = model.Detail;
        var age = snapshot is null ? (long?)null : Environment.TickCount64 - snapshot.PublishedTick;
        Set("Host snapshot age", age is null ? null : age < 0 ? "Invalid publication age" : age.Value.ToString(CultureInfo.InvariantCulture) + " ms");
        values["Host snapshot age"].ToolTip = "Host publication age, not provider observation age. No provider freshness timestamp is exposed.";
        Set("Provider availability", snapshot is null ? null : snapshot.Available ? "Reported available" : "Reported unavailable"); Set("Context", model.ContextLabel);
        Set("Desktop assignments", snapshot is null ? null : "Personal: " + snapshot.PersonalAssignment + " · Work: " + snapshot.WorkAssignment);
        Set("Desktop GUIDs", snapshot is null ? null : "Personal: " + snapshot.Settings.Personal + "\nWork: " + snapshot.Settings.Work);
        Set("Observation consent", model.ConsentStatus);
        Set("Recorded allowance", model.HeroLabel + ": " + model.HeroValue + " " + model.RecordedQualifier);
        Set("Recorded usage", model.Arithmetic);
        Set("Estimated portion", recorded?.State.EstimatedUsageMs is { } estimated ? estimated.ToString(CultureInfo.InvariantCulture) + " ms " + model.RecordedQualifier : null);
        Set("Qualifying break", model.BreakValue + " " + model.RecordedQualifier);
        Set("Intervention", recorded?.State.Intervention is { } intervention ? intervention + " " + model.RecordedQualifier : null);
        Set("Reminder remainder", model.ShowReminder ? model.ReminderLabel + ": " + model.ReminderValue + " " + model.RecordedQualifier : "No applicable countdown");
        Set("Reminder presentation", main.NudgeStatus.Text.Length > 0 ? main.NudgeStatus.Text : snapshot?.ReminderStatus);
        Set("Checkpoint storage", model.Persistence); Set("Storage failure", snapshot?.StorageFailure?.ToString() ?? "None reported");
        Set("Windows settings", snapshot?.SettingsStatus); Set("Configuration", snapshot?.Configuration);
        Set("Last command", commands.LastCommand?.ToString() ?? "No command submitted yet");
        Set("Command elapsed", commands.IsPending ? commands.ElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms · No deadline promised" : "—");
        Set("Last result", commands.LastResult?.Message ?? "No confirmed result yet");
        Set("Sequence / epoch", snapshot is null ? null : snapshot.Sequence + " / " + snapshot.AccountingPresentationEpoch);
        Set("Reminder generation", snapshot is null ? null : snapshot.ReminderGeneration + " / " + snapshot.ReminderEpoch + " / " + snapshot.PresentationId);
        Set("Switching availability", snapshot?.SwitchingUnavailableReason); Set("Engine state", snapshot?.State);
        feedback.Text = commands.Message;
    }
}

internal sealed class DiagnosticsCompanion(UiPreferences preferences, Func<DiagnosticsWindow> create) : IDisposable
{
    internal DiagnosticsWindow? Window { get; private set; }
    internal bool SetEnabled(bool enabled)
    {
        if (!preferences.Save(enabled)) return false;
        if (!enabled) Window?.Close();
        return true;
    }
    internal void Open()
    {
        if (!preferences.ShowDiagnostics) return;
        if (Window is null)
        {
            Window = create();
            Window.Closed += OnClosed; Window.Show();
        }
        else { if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal; Window.Activate(); }
    }
    private void OnClosed(object? sender, EventArgs e)
    { if (sender is DiagnosticsWindow closed) closed.Closed -= OnClosed; Window = null; }
    public void Dispose() => Window?.Close();
}
