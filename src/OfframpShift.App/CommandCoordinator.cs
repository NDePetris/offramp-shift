using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

// UI submission/feedback only. Nudge transactions and Exit have their own owners.
internal sealed class CommandCoordinator
{
    private readonly Func<long> now;
    private long started;
    internal CommandCoordinator(Func<long>? now = null) => this.now = now ?? (() => Environment.TickCount64);
    internal event Action? Changed;
    internal HostCommandKind? Pending { get; private set; }
    internal HostCommandKind? LastCommand { get; private set; }
    internal HostCommandResult? LastResult { get; private set; }
    internal string Message { get; private set; } = "";
    internal bool IsPending => Pending is not null;
    internal long ElapsedMs => IsPending ? Math.Max(0, now() - started) : 0;
    internal void RefreshWaiting()
    {
        if (Pending is not null && now() - started >= 5_000 && !Message.Contains("Still waiting", StringComparison.Ordinal))
        { Message += " Still waiting for confirmation; you can keep using Windows."; Changed?.Invoke(); }
    }
    internal async Task<HostCommandResult> ExecuteAsync(HostCommand command, Func<HostCommand, Task<HostCommandResult>> execute)
    {
        if (IsPending) return HostCommandResult.Failure("Another action is awaiting confirmation. Wait for its result before submitting again.");
        Pending = LastCommand = command.Kind; started = now(); LastResult = null;
        Message = PendingText(command.Kind); Changed?.Invoke();
        HostCommandResult result;
        try { result = await execute(command); }
        catch (Exception e) when (e is not OutOfMemoryException)
        { result = HostCommandResult.Failure("The action could not be confirmed. Review monitoring and storage before trying again."); }
        Pending = null;
        Message = result.Message.Length > 0 ? result.Message : result.Succeeded ? CompletedText(command.Kind) : "The action did not complete.";
        LastResult = result with { Message = Message };
        Changed?.Invoke(); return LastResult;
    }
    internal static string PendingText(HostCommandKind kind) => kind switch
    {
        HostCommandKind.Pause => "Pause requested · Waiting for confirmation…",
        HostCommandKind.Resume => "Resume requested · Waiting for confirmation…",
        HostCommandKind.Start => "Starting monitoring · Waiting for confirmation…",
        HostCommandKind.RetryMonitoring => "Retrying monitoring · Waiting for fresh evidence…",
        HostCommandKind.RetrySave => "Retrying the exact pending save · Waiting for confirmation…",
        HostCommandKind.ReopenStorage => "Reopening storage · Waiting for confirmation…",
        HostCommandKind.Recover => "Recovering accounting · Waiting for confirmation…",
        HostCommandKind.ResetUsage => "Resetting usage · Waiting for save confirmation…",
        HostCommandKind.AssignPersonal or HostCommandKind.AssignWork => "Assigning desktop · Waiting for save confirmation…",
        HostCommandKind.RepairSettings => "Repairing Windows settings · Waiting for confirmation…",
        HostCommandKind.ChangeDurations => "Saving monitoring settings · Waiting for confirmation…",
        HostCommandKind.ChangeBreakReminders => "Saving reminder style · Waiting for confirmation…",
        HostCommandKind.ChangeInteractiveRepeat => "Saving repeat interval · Waiting for confirmation…",
        HostCommandKind.ChangeAppearance => "Saving appearance and closing · Waiting for confirmation…",
        HostCommandKind.ChangeSwitchingConsent => "Saving switching consent · Waiting for confirmation…",
        HostCommandKind.ChangeCloseBehavior => "Saving close preference · Waiting for confirmation…",
        _ => "Saving settings · Waiting for confirmation…"
    };
    private static string CompletedText(HostCommandKind kind) => kind switch
    {
        HostCommandKind.Pause => "Monitoring paused.",
        HostCommandKind.Resume => "Monitoring resumed; fresh evidence is required.",
        HostCommandKind.Start => "Monitoring started; fresh evidence is required.",
        HostCommandKind.RetrySave => "Pending save confirmed.",
        HostCommandKind.ReopenStorage => "Storage reopened; review monitoring status.",
        _ => "Action confirmed."
    };
}
