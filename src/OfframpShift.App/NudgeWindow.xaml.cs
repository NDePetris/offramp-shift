using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

public partial class NudgeWindow : Window
{
    private readonly Func<long, Task<HostCommandResult>> defer;
    private bool automatic, submitted, closed;
    internal NudgeWindow(Func<long, Task<HostCommandResult>> defer)
    {
        InitializeComponent();
        this.defer = defer;
        // Establish logical focus before an HWND is shown. Enter is not an extension.
        FocusManager.SetFocusedElement(this, NudgeHeading);
        Closing += OnClosing;
        Closed += (_, _) => closed = true;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        ApplyBrand();
    }
    internal void ApplyBrand()
    {
        var dark = BrandAssets.IsDark(Background);
        Icon = BrandAssets.WindowIcon(dark);
        FamilySymbol.Source = BrandAssets.Symbol(dark);
        FamilySymbol.Visibility = SystemParameters.HighContrast ? Visibility.Collapsed : Visibility.Visible;
    }
    internal void CloseAutomatically() { automatic = true; Close(); }
    private async void AddOne(object sender, RoutedEventArgs e) => await Submit(60_000);
    private async void AddFive(object sender, RoutedEventArgs e) => await Submit(300_000);
    internal async Task Submit(long duration)
    {
        if (submitted) return;
        submitted = true; OneMinute.IsEnabled = FiveMinutes.IsEnabled = false;
        var result = await defer(duration);
        if (closed) return;
        if (result.Succeeded) CloseAutomatically();
        else
        {
            submitted = false; OneMinute.IsEnabled = FiveMinutes.IsEnabled = true;
            Result.Text = "Extra time was not saved. " + result.Message;
            System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(Result)?.RaiseAutomationEvent(
                System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // X/Alt+F4/Escape always dismiss, including during an in-flight submission.
        if (!automatic && !submitted) { submitted = true; _ = defer(60_000); }
    }
}
