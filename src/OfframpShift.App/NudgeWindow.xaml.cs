using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using OfframpShift.ApplicationHost;
using DesktopFeasibility;

namespace OfframpShift.App;

public partial class NudgeWindow : Window
{
    private readonly Func<long, Task<HostCommandResult>> defer;
    private readonly Func<Task<SwitchCompletion>> switchToWork;
    private readonly Action? openMain;
    private (bool Dark, bool HighContrast)? artwork;
    private bool switchingDisabled;
    private string switchingUnavailableReason = "Switching availability is unknown.";
    private enum Closure { None, ContextInvalidated, CommandCompleted }
    private Closure closure;
    private bool submitted, closed;
    internal bool CanReportFailure => !closed && closure == Closure.None;
    internal NudgeWindow(Func<long, Task<HostCommandResult>> defer, Func<Task<SwitchCompletion>>? switchToWork = null, Action? openMain = null)
    {
        InitializeComponent();
        this.defer = defer;
        this.openMain = openMain;
        OpenMainButton.IsEnabled = openMain is not null;
        this.switchToWork = switchToWork ?? (() => Task.FromResult(SwitchCompletion.Reject("Switching is unavailable.")));
        // Establish logical focus before an HWND is shown. Enter is not an extension.
        FocusManager.SetFocusedElement(this, NudgeHeading);
        Closing += OnClosing;
        Closed += (_, _) => closed = true;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        ApplyBrand();
        SetSwitchAvailability(switchingUnavailableReason);
    }
    internal void ApplyBrand()
    {
        var dark = BrandAssets.IsDark(TryFindResource("WindowBackground") as System.Windows.Media.Brush);
        ApplyBrand(dark, SystemParameters.HighContrast);
    }
    internal void ApplyBrand(bool dark, bool highContrast)
    {
        if (artwork == (dark, highContrast)) return;
        artwork = (dark, highContrast);
        QuietRoadPalette.Apply(this, dark, highContrast);
        ReminderRoad.ApplyTheme(dark, highContrast);
        Icon = BrandAssets.WindowIcon(dark);
        FamilySymbol.Source = BrandAssets.Symbol(dark);
        FamilySymbol.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OpenMain(object sender, RoutedEventArgs e)
    {
        if (closed || closure != Closure.None) return;
        // Navigation deliberately leaves the authorized reminder HWND alive.
        // Closing here would invoke the existing durable one-minute deferral.
        openMain?.Invoke();
    }
    internal void SetSwitchAvailability(string reason)
    {
        switchingUnavailableReason = reason;
        var message = (reason.Length == 0 ? "Experimental switching uses one explicit attempt." : reason) +
            " Press Win+Tab and select Work manually at any time.";
        if (SwitchAvailability.Text != message)
        {
            SwitchAvailability.Text = message;
            SwitchWarning.Text = reason.Length == 0 ? "Experimental switching · one explicit attempt." : reason;
            System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(SwitchWarning)?.RaiseAutomationEvent(
                System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        Controls(!submitted && !closed);
    }
    internal void CloseAutomatically()
    {
        if (closed) return;
        closure = Closure.ContextInvalidated;
        Close();
    }
    private async void AddOne(object sender, RoutedEventArgs e) => await Submit(60_000);
    private async void AddFive(object sender, RoutedEventArgs e) => await Submit(300_000);
    private async void SwitchToWork(object sender, RoutedEventArgs e) => await SubmitSwitch();
    private void Controls(bool enabled)
    {
        OneMinute.IsEnabled = FiveMinutes.IsEnabled = enabled;
        SwitchWork.IsEnabled = enabled && !switchingDisabled && switchingUnavailableReason.Length == 0;
        SwitchWork.Visibility = switchingUnavailableReason.Length == 0 && !switchingDisabled ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Announce(string message)
    {
        Result.Text = message;
        System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(Result)?.RaiseAutomationEvent(
            System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    internal async Task SubmitSwitch()
    {
        if (submitted || closed || closure != Closure.None || switchingDisabled || switchingUnavailableReason.Length != 0) return;
        submitted = true; Controls(false); Announce("Checking and switching to Work…");
        SwitchCompletion result;
        try { result = await switchToWork(); }
        catch (Exception) { result = SwitchCompletion.Unknown("Switch operation failed."); }
        if (closed) return;
        if (result.Outcome == SwitchOutcome.ConfirmedSuccess)
        { closure = Closure.CommandCompleted; Close(); return; }
        switchingDisabled = result.Outcome == SwitchOutcome.Indeterminate;
        submitted = false; Controls(true); Announce(result.Message);
    }
    internal Task Submit(long duration) => Submit(duration, fromClosing: false);
    private async Task Submit(long duration, bool fromClosing)
    {
        if (submitted || closed || closure != Closure.None) return;
        submitted = true; Controls(false);
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
            submitted = false; Controls(true);
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
