using System.Windows.Interop;
using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

internal interface INudgePlatform
{
    nint ForegroundWindow { get; }
    string PlacementStatus => "Unavailable";
    bool Place(NudgeWindow window, nint foreground);
    void Show(NudgeWindow window);
    bool Activate(NudgeWindow window);
}

// Dispatcher-owned. Only an accepted event may show/activate; Refresh only closes.
internal sealed class NudgePresenter(Func<HostCommand, Task<HostCommandResult>> execute,
    Func<HostSnapshot?> latest, Action<string> reportFailure, INudgePlatform platform)
{
    internal NudgeWindow? Window { get; private set; }
    internal string Status { get; private set; } = "No interactive reminder consumed yet.";
    private bool presenting, submitting;
    private long lastSequence;
    private Guid lastEpoch;
    internal void Refresh(bool stopped = false)
    {
        var snapshot = latest();
        if (stopped || snapshot is null || !VisibleEligible(snapshot))
        {
            if (Window is not null) Status = "Window closed: monitoring, context, intervention or freshness no longer eligible.";
            Close();
        }
        else Window?.ApplyBrand();
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
        try
        {
            Status = "Committed reminder consumed.";
            if (!Eligible(value)) { Status = "Consumed reminder suppressed: context, generation, foreground or freshness changed."; return; }
            var firstShow = Window is null;
            if (Window is null)
            {
                NudgeWindow? candidate = null;
                candidate = new(duration => DeferAsync(candidate!, duration));
                Window = candidate;
                candidate.Closed += (_, _) => { if (Window == candidate) Window = null; };
            }
            var target = Window;
            var handle = new WindowInteropHelper(target).EnsureHandle();
            var result = await execute(new(HostCommandKind.AuthorizeNudge) { Reminder = value, NudgeWindow = handle, PrepareOnly = firstShow });
            if (!result.Succeeded) { Status = "Authorization rejected before show: " + result.Message; Close(); return; }
            if (Window != target || !Eligible(value)) { Status = "Authorized reminder expired or changed before placement."; Close(); return; }
            if (!platform.Place(target, value.ForegroundWindow)) { Status = "Positioning failed: " + platform.PlacementStatus + "."; Close(); return; }
            // Showing must not activate implicitly. Recheck after layout/display changes.
            platform.Show(target);
            if (Window != target || !Eligible(value)) { Status = "Reminder changed during nonactivating show."; Close(); return; }
            if (firstShow)
            {
                result = await execute(new(HostCommandKind.AuthorizeNudge) { Reminder = value, NudgeWindow = handle });
                if (!result.Succeeded) { Status = "Authorization rejected after show: " + result.Message; Close(); return; }
            }
            if (Window != target || !Eligible(value)) { Status = "Reminder changed before focus request."; Close(); return; }
            Status = platform.Activate(target) ? "Window shown; foreground request accepted." :
                "Window shown; Windows denied focus. Use its taskbar button or wait for the next eligible reminder.";
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        { Status = "Presentation failed: window or native operation unavailable."; Close(); reportFailure(Status + " Wait for the next eligible reminder."); }
        finally { presenting = false; }
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
            var result = await execute(new(HostCommandKind.DeferInteractive)
            { DurationMs = duration, NudgeWindow = new WindowInteropHelper(target).Handle });
            if (!result.Succeeded) reportFailure("Extra time was not saved. " + result.Message);
            return result;
        }
        finally { submitting = false; }
    }
}
