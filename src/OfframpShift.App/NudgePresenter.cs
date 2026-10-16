using System.Windows.Interop;
using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

internal interface INudgePlatform
{
    nint ForegroundWindow { get; }
    bool Place(NudgeWindow window, nint foreground);
    void Show(NudgeWindow window);
    bool Activate(NudgeWindow window);
}

// Dispatcher-owned. Only an accepted event may show/activate; Refresh only closes.
internal sealed class NudgePresenter(Func<HostCommand, Task<HostCommandResult>> execute,
    Func<HostSnapshot?> latest, Action<string> reportFailure, INudgePlatform platform)
{
    internal NudgeWindow? Window { get; private set; }
    private bool presenting, submitting;
    private long lastSequence;
    private Guid lastEpoch;
    internal void Refresh(bool stopped = false)
    {
        var snapshot = latest();
        if (stopped || snapshot is null || !VisibleEligible(snapshot)) Close();
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
            if (!Eligible(value)) return;
            if (Window is null)
            {
                NudgeWindow? candidate = null;
                candidate = new(duration => DeferAsync(candidate!, duration));
                Window = candidate;
                candidate.Closed += (_, _) => { if (Window == candidate) Window = null; };
            }
            var target = Window;
            var handle = new WindowInteropHelper(target).EnsureHandle();
            var result = await execute(new(HostCommandKind.AuthorizeNudge) { Reminder = value, NudgeWindow = handle });
            if (!result.Succeeded || Window != target || !Eligible(value) || !platform.Place(target, value.ForegroundWindow))
            { Close(); return; }
            // Showing must not activate implicitly. Recheck after layout/display changes.
            platform.Show(target);
            if (Window != target || !Eligible(value)) { Close(); return; }
            platform.Activate(target); // Best effort, exactly once. Never retry or toggle topmost.
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        { Close(); reportFailure("The nudge could not be presented safely. " + "Wait for the next eligible reminder."); }
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
