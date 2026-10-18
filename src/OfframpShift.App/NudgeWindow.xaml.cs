using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

public partial class NudgeWindow : Window
{
    private readonly Func<long, Task<HostCommandResult>> defer;
    private enum Closure { None, ContextInvalidated, CommandCompleted }
    private Closure closure;
    private bool submitted, closed;
    internal bool CanReportFailure => !closed && closure == Closure.None;
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
    internal void CloseAutomatically()
    {
        if (closed) return;
        closure = Closure.ContextInvalidated;
        Close();
    }
    private async void AddOne(object sender, RoutedEventArgs e) => await Submit(60_000);
    private async void AddFive(object sender, RoutedEventArgs e) => await Submit(300_000);
    internal Task Submit(long duration) => Submit(duration, fromClosing: false);
    private async Task Submit(long duration, bool fromClosing)
    {
        if (submitted || closed || closure != Closure.None) return;
        submitted = true; OneMinute.IsEnabled = FiveMinutes.IsEnabled = false;
        Result.Text = "Saving extra time…";
        // Closing must unwind before even an immediately completed command can
        // call Close again. Reserve submission before yielding to prevent duplicates.
        if (fromClosing) await Task.Yield();
        if (closed || closure != Closure.None) return;
        HostCommandResult result;
        try { result = await defer(duration); }
        catch (Exception) { result = HostCommandResult.Failure("The extra-time request could not complete. Try again."); }
        if (closed) return;
        if (result.Succeeded)
        {
            closure = Closure.CommandCompleted;
            Close();
        }
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
        if (closure != Closure.None) return;
        // X/Alt+F4/Escape share Add 1 minute's authorization and durable result.
        // Retain the HWND and context until success, including during Add 5 minutes.
        e.Cancel = true;
        _ = Submit(60_000, fromClosing: true);
    }
}
