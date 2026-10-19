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
    bool Activate(NudgeWindow window);
}

// Dispatcher-owned. Only an accepted event may show/activate; Refresh only closes.
internal sealed class NudgePresenter(Func<HostCommand, Task<HostCommandResult>> execute,
    Func<HostSnapshot?> latest, Action<string> reportFailure, INudgePlatform platform,
    Func<nint, CancellationToken, Task<SwitchCompletion>>? switchToWork = null)
{
    internal NudgeWindow? Window { get; private set; }
    internal string Status { get; private set; } = "No interactive reminder consumed yet.";
    private bool presenting, submitting;
    private long lastSequence;
    private Guid lastEpoch;
    internal void Refresh(bool stopped = false)
    {
        if (presenting && !stopped) return; // Completion independently revalidates fresh safety.
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
    }
    private static bool VisibleEligible(HostSnapshot value) => value.CanKeepNudgeOpen &&
        value.Settings.ReminderStyle == ReminderStyle.InteractiveWindow &&
        Environment.TickCount64 - value.PublishedTick is >= 0 and <= 2_000;
    internal void Close() { var previous = Window; Window = null; previous?.CloseAutomatically(); }
    internal async Task PresentAsync(ReminderEvent value)
    {
        if (presenting || submitting) return;
        if (value.Epoch != lastEpoch) { lastEpoch = value.Epoch; lastSequence = 0; }
        if (value.Sequence <= lastSequence) return;
        lastSequence = value.Sequence;
        presenting = true;
        var elapsed = Stopwatch.StartNew(); long beginMs = 0, showMs = 0, confirmMs = 0;
        try
        {
            Status = "Committed reminder consumed.";
            if (!Eligible(value)) { Status = "Consumed reminder suppressed: context, generation, foreground or freshness changed."; return; }
            // Accept the committed trigger before construction can emit native
            // window/foreground events. Both new and reused HWNDs follow one path.
            var result = await execute(new(HostCommandKind.BeginNudgePresentation) { Reminder = value });
            beginMs = elapsed.ElapsedMilliseconds;
            if (!result.Succeeded) { Status = "Authorization rejected before creation: " + result.Message; Close(); return; }
            var transaction = result.Presentation;
            if (transaction is null) { Status = "Presentation rejected before creation: MissingTransaction."; Close(); return; }
            var rejection = PresentationRejection(transaction, 0);
            if (rejection is not null)
            { Status = "Presentation rejected before creation: " + rejection + "."; Close(); return; }
            if (Window is null)
            {
                NudgeWindow? candidate = null;
                candidate = new(duration => DeferAsync(candidate!, duration), () => SwitchAsync(candidate!));
                Window = candidate;
                candidate.Closed += (_, _) => { if (Window == candidate) Window = null; };
            }
            var target = Window;
            target.SetSwitchAvailability(latest()?.SwitchingUnavailableReason ?? "Switching availability is unknown.");
            var handle = new WindowInteropHelper(target).EnsureHandle();
            var shown = await platform.PresentAsync(target, value.ForegroundWindow);
            showMs = elapsed.ElapsedMilliseconds - beginMs;
            if (!shown) { Status = "Positioning failed: " + platform.PlacementStatus + "."; Close(); return; }
            if (Window != target) { Status = "Window closed during nonactivating show."; Close(); return; }
            result = await execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = transaction, NudgeWindow = handle });
            confirmMs = elapsed.ElapsedMilliseconds - beginMs - showMs;
            if (!result.Succeeded) { Status = "Authorization rejected after show: " + result.Message; Close(); return; }
            rejection = Window != target ? "WindowClosed" : PresentationRejection(transaction, handle);
            if (rejection is not null)
            { Status = "Presentation rejected before focus: " + rejection + "."; Close(); return; }
            Status = platform.Activate(target) ? "Window shown; foreground request accepted." :
                "Window shown; Windows denied focus. Use its taskbar button or wait for the next eligible reminder.";
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        { Status = "Presentation failed: window or native operation unavailable."; Close(); reportFailure(Status + " Wait for the next eligible reminder."); }
        finally
        {
            Status += $" [begin={beginMs}ms; show/layout={showMs}ms; confirm={confirmMs}ms; total={elapsed.ElapsedMilliseconds}ms]";
            presenting = false;
        }
    }
    private string? PresentationRejection(NudgePresentation value, nint candidate)
    {
        if (Environment.TickCount64 > value.ExpiresTick) return "PresentationExpired";
        var snapshot = latest();
        if (snapshot is null) return "SnapshotUnavailable";
        if (Environment.TickCount64 - snapshot.PublishedTick is < 0 or > 2_000) return "SnapshotExpired";
        if (snapshot.Settings.ReminderStyle != ReminderStyle.InteractiveWindow) return "ReminderStyleChanged";
        if (snapshot.State?.Paused == true) return "Paused";
        if (!snapshot.CanKeepNudgeOpen) return "FreshPersonalContextUnavailable";
        if (snapshot.PresentationId != value.Id) return "TransactionRevoked";
        if (snapshot.ReminderEpoch != value.Epoch) return "RuntimeChanged";
        if (snapshot.Settings.Personal != value.PersonalDesktop) return "PersonalBindingChanged";
        return NudgePlatform.PresentationForegroundAllowed(platform.ForegroundWindow, value.OriginalForeground, candidate) ? null : "ExternalForeground";
    }
    private bool Eligible(ReminderEvent value) => value.Style == ReminderStyle.InteractiveWindow &&
        Environment.TickCount64 <= value.ExpiresTick && latest() is { } snapshot && VisibleEligible(snapshot) &&
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
