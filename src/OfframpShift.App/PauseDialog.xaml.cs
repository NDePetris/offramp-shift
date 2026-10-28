using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

public partial class PauseDialog : Window
{
    private readonly Func<DateTimeOffset> utcNow;
    private readonly TimeZoneInfo zone;
    private TimeSpan[] offsets = [];
    internal int SelectedChoice { get; private set; }
    internal HostCommand? Selection { get; private set; }

    internal PauseDialog(MainWindow main, Func<DateTimeOffset>? utcNow = null, TimeZoneInfo? zone = null)
    {
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.zone = zone ?? TimeZoneInfo.Local;
        InitializeComponent(); Resources = main.Resources; Icon = main.Icon;
        if (main.IsVisible) { Owner = main; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height);
        var initial = PauseSelection.NextQuarterHour(this.utcNow(), this.zone);
        Hour.ItemsSource = Enumerable.Range(1, 12).ToArray();
        Minute.ItemsSource = Enumerable.Range(0, 60).Select(minute => minute.ToString("00", CultureInfo.InvariantCulture)).ToArray();
        Period.ItemsSource = new[] { "AM", "PM" };
        Date.SelectedDate = initial.Date;
        Hour.SelectedIndex = (initial.Hour + 11) % 12;
        Minute.SelectedIndex = initial.Minute;
        Period.SelectedIndex = initial.Hour >= 12 ? 1 : 0;
        Zone.Text = PauseSelection.ZoneLabel(this.zone);
        FifteenMinutes.IsChecked = true;
        Loaded += (_, _) => { UpdateAvailableHeight(); FifteenMinutes.Focus(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(UpdateAvailableHeight);
    }

    private void UpdateAvailableHeight()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        MaxHeight = Math.Max(MinHeight, area.Height / VisualTreeHelper.GetDpi(this).DpiScaleY);
    }

    private void ChoiceChanged(object sender, RoutedEventArgs e)
    {
        if (Custom is null) return;
        SelectedChoice = int.Parse((string)((RadioButton)sender).Tag, CultureInfo.InvariantCulture);
        Custom.Visibility = SelectedChoice == 4 ? Visibility.Visible : Visibility.Collapsed;
        SizeToContent = SizeToContent.Height;
        Validation.Text = "";
    }
    private void DateChanged(object? sender, SelectionChangedEventArgs e) => RefreshOccurrences();
    private void TimeChanged(object sender, SelectionChangedEventArgs e) => RefreshOccurrences();
    private string SelectedTime()
    {
        if (Hour.SelectedIndex < 0 || Minute.SelectedIndex < 0 || Period.SelectedIndex < 0)
            throw new ArgumentException("Choose an hour, minute and AM or PM.");
        var hour = (Hour.SelectedIndex + 1) % 12 + (Period.SelectedIndex == 1 ? 12 : 0);
        return new TimeOnly(hour, Minute.SelectedIndex).ToString("t", CultureInfo.CurrentCulture);
    }
    private void RefreshOccurrences()
    {
        if (Occurrence is null) return;
        offsets = [];
        try
        {
            var local = PauseSelection.LocalTime(Date.SelectedDate, SelectedTime());
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
        if (TryConfirmSelection()) DialogResult = true;
    }

    internal bool TryConfirmSelection()
    {
        try
        {
            var deadline = PauseSelection.Deadline(SelectedChoice, Date.SelectedDate, SelectedChoice == 4 ? SelectedTime() : "", zone,
                Occurrence.SelectedIndex >= 0 ? offsets[Occurrence.SelectedIndex] : null, utcNow());
            Selection = new(HostCommandKind.Pause) { PauseUntilUtc = deadline };
            return true;
        }
        catch (ArgumentException error)
        {
            Validation.Text = error.Message;
            UIElementAutomationPeer.CreatePeerForElement(Validation)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            if (SelectedChoice == 4)
            {
                if (offsets.Length > 0 && Occurrence.SelectedIndex < 0) Occurrence.Focus();
                else Hour.Focus();
            }
            return false;
        }
    }
}
