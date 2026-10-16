namespace OfframpShift.Core;

// Store must atomically replace the entire checkpoint and report failures to its caller.
// The integration must commit a transition before delivering its advisory effects.
public interface ICheckpointStore
{
    MonitoringCheckpoint? Load();
    void Save(MonitoringCheckpoint checkpoint);
}

public sealed record MonitoringCheckpoint(
    int Version, DateTimeOffset SavedUtc, MonitoringConfiguration Configuration, EngineState State)
{
    public const int CurrentVersion = 3;

    public bool IsValid(DateTimeOffset now)
    {
        if (Configuration is null || State is null) return false;
        try { Configuration.Validate(); }
        catch (ArgumentException) { return false; }
        var s = State;
        // Age alone never expires confirmed usage. Future/default metadata is unusable.
        return (Version is CurrentVersion or 2 || Version == 1 && Configuration.Accounting == AccountingMode.CertifiedOnly &&
                s.EstimatedUsageMs == 0 && s.BreakUncertaintyMs == 0 && s.ConsecutiveUncertaintyMs == 0) &&
            SavedUtc != default && SavedUtc <= now &&
            s.EstimatedUsageMs >= 0 && s.EstimatedUsageMs <= s.UsageMs &&
            s.BreakUncertaintyMs >= 0 && s.BreakUncertaintyMs <= EstimationPolicy.MaximumBreakUncertaintyMs &&
            s.ConsecutiveUncertaintyMs >= 0 && s.ConsecutiveUncertaintyMs <= EstimationPolicy.MaximumConsecutiveUncertaintyMs &&
            s.ConsecutiveUncertaintyMs <= s.BreakUncertaintyMs &&
            (s.BreakProgressMs > 0 && !s.BreakCompleted || s.BreakUncertaintyMs == 0 && s.ConsecutiveUncertaintyMs == 0) &&
            Enum.IsDefined(s.Intervention) && (Version >= 3 || s.Intervention != InterventionState.PersonalDeferred) && s.Recovery == RecoveryStatus.Ready &&
            s.UsageMs >= 0 && (s.Exhausted ? s.UsageMs > 0 : s.UsageMs < Configuration.AllowanceMs) &&
            s.BreakProgressMs >= 0 && s.BreakProgressMs <= Configuration.RequiredBreakMs &&
            s.BreakCompleted == (s.BreakProgressMs == Configuration.RequiredBreakMs) &&
            (!s.BreakCompleted || s.UsageMs == 0) &&
            s.IdleProgressMs >= 0 && s.IdleProgressMs <= Configuration.IdleThresholdMs &&
            (!s.Paused || (s.BreakProgressMs == 0 && s.IdleProgressMs == 0)) &&
            s.EffectSequence >= 0 &&
            s.ReminderRemainingMs >= 0 && s.SnoozeRemainingMs >= 0 &&
            (s.Exhausted
                ? s.Intervention != InterventionState.Normal &&
                    s.ReminderRemainingMs <= (s.Intervention == InterventionState.Grace ? Configuration.GraceMs :
                        Version >= 3 ? Math.Max(Configuration.RepeatNudgeMs, Configuration.InteractiveRepeatNudgeMs) : Configuration.RepeatNudgeMs) &&
                    (s.Intervention is InterventionState.Snoozed or InterventionState.PersonalDeferred
                        ? s.ReminderRemainingMs == 0 && s.SnoozeRemainingMs > 0 && s.SnoozeRemainingMs <=
                            (s.Intervention == InterventionState.PersonalDeferred ? 300_000 : Configuration.SnoozeMs)
                        : s.SnoozeRemainingMs == 0)
                : s.Intervention == InterventionState.Normal && s.ReminderRemainingMs == 0 && s.SnoozeRemainingMs == 0);
    }
}
