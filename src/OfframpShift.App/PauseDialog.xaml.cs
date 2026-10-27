using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

public partial class PauseDialog : Window
{
    private readonly Func<DateTimeOffset> utcNow;
    private readonly TimeZoneInfo zone;
    private TimeSpan[] offsets = [];
    internal HostCommand? Selection { get; private set; }

    internal PauseDialog(MainWindow main, Func<DateTimeOffset>? utcNow = null, TimeZoneInfo? zone = null)
    {
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.zone = zone ?? TimeZoneInfo.Local;
        InitializeComponent(); Resources = main.Resources; Icon = main.Icon;
        if (main.IsVisible) { Owner = main; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        var initial = TimeZoneInfo.ConvertTime(this.utcNow().AddMinutes(15), this.zone);
        Date.SelectedDate = initial.Date;
        Time.Text = initial.ToString("t", CultureInfo.CurrentCulture);
        Zone.Text = "Local time: " + this.zone.DisplayName;
        Choice.ItemsSource = PauseSelection.Choices; Choice.SelectedIndex = 0;
        Loaded += (_, _) => Choice.Focus();
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Custom is null) return;
        Custom.Visibility = Choice.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        Validation.Text = "";
    }
    private void DateChanged(object? sender, SelectionChangedEventArgs e) => RefreshOccurrences();
    private void TimeChanged(object sender, TextChangedEventArgs e) => RefreshOccurrences();
    private void RefreshOccurrences()
    {
        if (Occurrence is null) return;
        offsets = [];
        try
        {
            var local = PauseSelection.LocalTime(Date.SelectedDate, Time.Text);
            if (zone.IsAmbiguousTime(local)) offsets = zone.GetAmbiguousTimeOffsets(local).OrderDescending().ToArray();
        }
        catch (ArgumentException) { }
        Occurrence.ItemsSource = offsets.Select((offset, index) =>
            $"{(index == 0 ? "First" : "Second")} occurrence (UTC{(offset < TimeSpan.Zero ? "−" : "+")}{offset.Duration():hh\\:mm})").ToArray();
        Occurrence.SelectedIndex = -1;
        Ambiguity.Visibility = offsets.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Validation.Text = "";
    }
    private void Confirm(object sender, RoutedEventArgs e)
    {
        try
        {
            var deadline = PauseSelection.Deadline(Choice.SelectedIndex, Date.SelectedDate, Time.Text, zone,
                Occurrence.SelectedIndex >= 0 ? offsets[Occurrence.SelectedIndex] : null, utcNow());
            Selection = new(HostCommandKind.Pause) { PauseUntilUtc = deadline };
            DialogResult = true;
        }
        catch (ArgumentException error)
        {
            Validation.Text = error.Message;
            UIElementAutomationPeer.CreatePeerForElement(Validation)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            if (Choice.SelectedIndex == 4)
            {
                if (offsets.Length > 0 && Occurrence.SelectedIndex < 0) Occurrence.Focus();
                else Time.Focus();
            }
        }
    }
}
