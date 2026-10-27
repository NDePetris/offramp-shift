using System.Windows.Interop;
using System.Diagnostics;
using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;
using DesktopFeasibility;

namespace OfframpShift.App;

internal interface INudgePlatform
{
    nint ForegroundWindow { get; }
    string PlacementStatus => "Unavailable";
    Task<bool> PresentAsync(NudgeWindow window, nint foreground);
    bool IsVisible(NudgeWindow window) => window.IsVisible;
    bool RaiseWithoutActivation(NudgeWindow window);
    bool Activate(NudgeWindow window);
    NudgeObservation Observe(NudgeWindow window, nint original) => new();
}

// Identity-free, latest-result-only diagnostics. Native visibility is not proof of unobscured pixels.
internal sealed record NudgeObservation(string ForegroundRole = "Unavailable", bool? Visible = null,
    bool? Minimized = null, bool? Topmost = null, bool? OnOriginalMonitor = null, uint? Dpi = null);
internal sealed record NudgePresentationResult(bool ShowLayout = false, string Authorization = "NotAttempted",
    string Raise = "NotAttempted", string ForegroundRequest = "NotAttempted",
    NudgeObservation? AfterShow = null, NudgeObservation? AfterRequest = null);

// Dispatcher-owned. Only an accepted event may show/raise/activate; Refresh only updates/closes.
internal sealed class NudgePresenter(Func<HostCommand, Task<HostCommandResult>> execute,
    Func<HostSnapshot?> latest, Action<string> reportFailure, INudgePlatform platform,
    Func<nint, CancellationToken, Task<SwitchCompletion>>? switchToWork = null, Action? openMain = null, Func<long>? tickCount = null)
{
    private long Now => tickCount?.Invoke() ?? Environment.TickCount64;
    internal NudgeWindow? Window { get; private set; }
    internal string Status { get; private set; } = "No interactive reminder consumed yet.";
    internal NudgePresentationResult LastPresentation { get; private set; } = new();
    internal string DiagnosticStatus => Status + $" [show/layout={LastPresentation.ShowLayout}; authorization={LastPresentation.Authorization}; raise={LastPresentation.Raise}; foreground={LastPresentation.ForegroundRequest}; afterShow={LastPresentation.AfterShow}; afterRequest={LastPresentation.AfterRequest}]";
    internal event Action? OpenStateChanged;
    internal bool HasOpenReminder => Window is { } window && platform.IsVisible(window) && latest() is { } snapshot && VisibleEligible(snapshot);
    private bool presenting, submitting;
    private long lastSequence;
    private Guid lastEpoch;
    internal void Refresh(bool stopped = false)
    {
        if (presenting && !stopped)
        {
            OpenStateChanged?.Invoke(); // Passive hint still follows eligibility while completion is pending.
            return; // Completion independently revalidates fresh safety.
        }
        var snapshot = latest();
        if (stopped || snapshot is null || !VisibleEligible(snapshot))
        {
            if (Window is not null) Status = "Window closed: monitoring, context, intervention or freshness no longer eligible.";
            Close();
        }
        else if (Window is { } window)
        {
            window.SetSwitchAvailability(snapshot.SwitchingUnavailableReason);
            window.ApplyBrand();
        }
        OpenStateChanged?.Invoke();
    }
    private bool VisibleEligible(HostSnapshot value) => value.CanKeepNudgeOpen &&
        value.Settings.ReminderStyle == ReminderStyle.InteractiveWindow &&
        Now - value.PublishedTick is >= 0 and <= 2_000;
    internal void Close() { var previous = Window; Window = null; previous?.CloseAutomatically(); OpenStateChanged?.Invoke(); }
    internal async Task PresentAsync(ReminderEvent value)
    {
        if (presenting || submitting) return;
        if (value.Epoch != lastEpoch) { lastEpoch = value.Epoch; lastSequence = 0; }
        if (value.Sequence <= lastSequence) return;
        lastSequence = value.Sequence;
        presenting = true;
        LastPresentation = new();
        var elapsed = Stopwatch.StartNew(); long beginMs = 0, showMs = 0, confirmMs = 0;
        try
        {
            Status = "Committed reminder consumed.";
            if (!Eligible(value)) { Status = "Consumed reminder suppressed: context, generation, foreground or freshness changed."; return; }
            // Accept the committed trigger before construction can emit native
            // window/foreground events. Both new and reused HWNDs follow one path.
            var result = await execute(new(HostCommandKind.BeginNudgePresentation) { Reminder = value });
            beginMs = elapsed.ElapsedMilliseconds;
            if (!result.Succeeded) { LastPresentation = LastPresentation with { Authorization = "BeginRejected" }; Status = "Authorization rejected before creation: " + result.Message; Close(); return; }
            var transaction = result.Presentation;
            if (transaction is null) { LastPresentation = LastPresentation with { Authorization = "MissingTransaction" }; Status = "Presentation rejected before creation: MissingTransaction."; Close(); return; }
            var rejection = PresentationRejection(transaction, 0);
            if (rejection is not null)
            { LastPresentation = LastPresentation with { Authorization = rejection }; Status = "Presentation rejected before creation: " + rejection + "."; Close(); return; }
            if (Window is null)
            {
                NudgeWindow? candidate = null;
                candidate = new(duration => DeferAsync(candidate!, duration), () => SwitchAsync(candidate!), openMain);
                Window = candidate;
                candidate.Closed += (_, _) => { if (Window == candidate) Window = null; OpenStateChanged?.Invoke(); };
            }
            var target = Window;
            target.SetSwitchAvailability(latest()?.SwitchingUnavailableReason ?? "Switching availability is unknown.");
            var handle = new WindowInteropHelper(target).EnsureHandle();
            var shown = await platform.PresentAsync(target, value.ForegroundWindow);
            showMs = elapsed.ElapsedMilliseconds - beginMs;
            if (!shown) { Status = "Positioning failed: " + platform.PlacementStatus + "."; Close(); return; }
            LastPresentation = LastPresentation with { ShowLayout = true, AfterShow = Observe(target, value.ForegroundWindow) };
            if (Window != target) { Status = "Window closed during nonactivating show."; Close(); return; }
            result = await execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = transaction, NudgeWindow = handle });
            confirmMs = elapsed.ElapsedMilliseconds - beginMs - showMs;
            if (!result.Succeeded) { LastPresentation = LastPresentation with { Authorization = "CompletionRejected" }; Status = "Authorization rejected after show: " + result.Message; Close(); return; }
            rejection = TargetRejection(target, handle, transaction);
            if (rejection is not null)
            { LastPresentation = LastPresentation with { Authorization = rejection }; Status = "Presentation rejected before raise: " + rejection + "."; Close(); return; }
            LastPresentation = LastPresentation with { Authorization = "Allowed", Raise = "Attempted" };
            // No await/yield: one ordinary non-topmost raise, then fresh checks before focus.
            bool raised;
            try { raised = platform.RaiseWithoutActivation(target); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
            { raised = false; }
            LastPresentation = LastPresentation with { Raise = raised ? "Succeeded" : "Failed" };
            rejection = TargetRejection(target, handle, transaction);
            if (rejection is not null)
            { LastPresentation = LastPresentation with { Authorization = rejection }; Status = "Presentation rejected after raise: " + rejection + "."; Close(); return; }
            LastPresentation = LastPresentation with { ForegroundRequest = "Attempted" };
            var accepted = platform.Activate(target);
            LastPresentation = LastPresentation with { ForegroundRequest = accepted ? "Accepted" : "Refused", AfterRequest = Observe(target, value.ForegroundWindow) };
            Status = "Show/layout completed; foreground request " + (accepted ? "accepted" : "refused") +
                ". Visibility and focus are best effort; select its taskbar button to interact.";
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        { Status = "Presentation failed: window or native operation unavailable."; Close(); reportFailure(Status + " Wait for the next eligible reminder."); }
        finally
        {
            Status += $" [begin={beginMs}ms; show/layout={showMs}ms; confirm={confirmMs}ms; total={elapsed.ElapsedMilliseconds}ms]";
            presenting = false;
            OpenStateChanged?.Invoke();
        }
    }
    private NudgeObservation Observe(NudgeWindow target, nint original)
    {
        try { return platform.Observe(target, original); }
        catch (Exception) { return new(); } // Diagnostics never authorize, fail presentation or change accounting.
    }
    private string? TargetRejection(NudgeWindow target, nint handle, NudgePresentation transaction) =>
        Window != target || new WindowInteropHelper(target).Handle != handle ? "WindowClosed" : PresentationRejection(transaction, handle);
    private string? PresentationRejection(NudgePresentation value, nint candidate)
    {
        if (Now > value.ExpiresTick) return "PresentationExpired";
        var snapshot = latest();
        if (snapshot is null) return "SnapshotUnavailable";
        if (Now - snapshot.PublishedTick is < 0 or > 2_000) return "SnapshotExpired";
        if (snapshot.Settings.ReminderStyle != ReminderStyle.InteractiveWindow) return "ReminderStyleChanged";
        if (snapshot.State?.Paused == true) return "Paused";
        if (!snapshot.CanKeepNudgeOpen) return "FreshPersonalContextUnavailable";
        if (snapshot.PresentationId != value.Id) return "TransactionRevoked";
        if (snapshot.ReminderEpoch != value.Epoch) return "RuntimeChanged";
        if (snapshot.Settings.Personal != value.PersonalDesktop) return "PersonalBindingChanged";
        return NudgePlatform.PresentationForegroundAllowed(platform.ForegroundWindow, value.OriginalForeground, candidate) ? null : "ExternalForeground";
    }
    private bool Eligible(ReminderEvent value) => value.Style == ReminderStyle.InteractiveWindow &&
        Now <= value.ExpiresTick && latest() is { } snapshot && VisibleEligible(snapshot) &&
        snapshot.ReminderGeneration == value.Generation && snapshot.ReminderEpoch == value.Epoch &&
        snapshot.Settings.Personal == value.PersonalDesktop && platform.ForegroundWindow == value.ForegroundWindow;
    private async Task<HostCommandResult> DeferAsync(NudgeWindow target, long duration)
    {
        if (submitting) return HostCommandResult.Failure("An extra-time request is already in progress.");
        submitting = true;
        try
        {
            HostCommandResult result;
            try
            {
                result = await execute(new(HostCommandKind.DeferInteractive)
                { DurationMs = duration, NudgeWindow = new WindowInteropHelper(target).Handle });
            }
            catch (Exception) { result = HostCommandResult.Failure("The extra-time request could not complete. Try again."); }
            // Refresh may already have closed this window after a genuine context
            // change or shutdown. Do not turn that cancellation into a tray error.
            // Storage failures still need explicit feedback even if Refresh closed it.
            if (!result.Succeeded && latest() is { } snapshot &&
                (snapshot.Persistence != PersistenceStatus.Ready || Window == target && target.CanReportFailure && VisibleEligible(snapshot)))
                reportFailure("Extra time was not saved. " + result.Message);
            return result;
        }
        finally { submitting = false; }
    }
    private async Task<SwitchCompletion> SwitchAsync(NudgeWindow target)
    {
        if (submitting || Window != target) return SwitchCompletion.Reject("This nudge is no longer active.");
        if (latest() is not { SwitchingUnavailableReason.Length: 0 })
            return SwitchCompletion.Reject("Switching is currently unavailable.");
        submitting = true;
        using var lifetime = new CancellationTokenSource();
        EventHandler closed = (_, _) => lifetime.Cancel();
        target.Closed += closed;
        try
        {
            SwitchCompletion result;
            try
            {
                result = switchToWork is null ? SwitchCompletion.Reject("Switching is unavailable.") :
                    await switchToWork(new WindowInteropHelper(target).Handle, lifetime.Token);
            }
            catch (Exception) { result = SwitchCompletion.Unknown("Switch operation could not complete."); }
            Status = result.Message;
            // Context monitoring may close the originating HWND before completion.
            // Preserve every unsuccessful result through the existing retained local
            // feedback path, including uncertainty after a transition to Work.
            if (result.Outcome != SwitchOutcome.ConfirmedSuccess) reportFailure(result.Message);
            return result;
        }
        finally { target.Closed -= closed; submitting = false; }
    }
}
