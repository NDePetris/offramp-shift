using System.Collections.Immutable;

namespace OfframpShift.Core;

// All entry points serialize through one gate. No callbacks, OS calls or I/O run here.
public sealed class MonitoringCoordinator
{
    private readonly object gate = new();
    private readonly IMonitoringClock clock;
    private MonitoringConfiguration configuration;
    private EngineState state;
    private MonotonicTime boundary;
    private MonotonicTime lastReceipt;
    private MonitoringObservation? lastObservation;

    public MonitoringCoordinator(IMonitoringClock clock, MonitoringConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        this.clock = clock;
        this.configuration = configuration;
        state = Fresh();
        boundary = lastReceipt = clock.Now;
        if (!ValidTime(boundary)) throw new ArgumentException("Invalid initial clock.", nameof(clock));
    }

    public EngineState State { get { lock (gate) return state; } }
    public MonitoringConfiguration Configuration { get { lock (gate) return configuration; } }

    // Missing means recovery required, not a first run. Caller chooses the constructor
    // only for an explicitly established first run, or Reinitialize after user action.
    public static MonitoringCoordinator Restore(IMonitoringClock clock, MonitoringConfiguration fallback,
        MonitoringCheckpoint? checkpoint)
    {
        var engine = new MonitoringCoordinator(clock, fallback);
        if (checkpoint is null || checkpoint.Configuration is null || checkpoint.State is null ||
            !checkpoint.IsValid(clock.UtcNow))
        {
            engine.state = Fresh() with { Recovery = RecoveryStatus.Required };
            return engine;
        }
        engine.configuration = checkpoint.Configuration;
        engine.state = checkpoint.State with
        {
            // Retain the completed-break latch, never resume an unfinished streak.
            BreakProgressMs = checkpoint.State.BreakCompleted ? checkpoint.Configuration.RequiredBreakMs : 0,
            IdleProgressMs = 0,
            BreakUncertaintyMs = 0,
            ConsecutiveUncertaintyMs = 0
        };
        return engine;
    }

    public MonitoringCheckpoint CreateCheckpoint()
    {
        lock (gate)
        {
            if (state.Recovery != RecoveryStatus.Ready)
                throw new InvalidOperationException("Explicit recovery is required before checkpointing.");
            return new(MonitoringCheckpoint.CurrentVersion, clock.UtcNow, configuration, state);
        }
    }

    // Explicit destructive domain operation: the future UI must label the lost history.
    // It is available only in recovery-required state and never called by Restore.
    public TransitionResult Reinitialize(MonitoringConfiguration replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        replacement.Validate();
        lock (gate)
        {
            if (state.Recovery != RecoveryStatus.Required)
                throw new InvalidOperationException("Reinitialization requires unusable-checkpoint recovery.");
            var now = clock.Now;
            if (!ValidTime(now)) throw new InvalidOperationException("A valid monotonic clock is required.");
            configuration = replacement;
            state = Fresh();
            boundary = lastReceipt = now;
            lastObservation = null;
            return Result(ActivityKind.Uncertain, "Explicit reinitialization; previous history abandoned.", [], true);
        }
    }

    public TransitionResult Observe(MonitoringObservation observation, bool interactive = false)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (gate)
        {
            if (state.Recovery == RecoveryStatus.Required)
                return Result(ActivityKind.Uncertain, "Recovery required.", [], false);
            var before = state;
            var now = clock.Now;
            if (!AcceptClock(now)) return Reject("Monotonic clock discontinuity.", now, before);
            if (observation == lastObservation)
                return Result(ActivityKind.Uncertain, "Duplicate observation ignored.", [], false);
            if (!ValidTime(observation.Start) || !ValidTime(observation.End) ||
                observation.Start.Epoch != now.Epoch || observation.End.Epoch != now.Epoch ||
                observation.End.Milliseconds <= observation.Start.Milliseconds ||
                observation.Start.Milliseconds < boundary.Milliseconds || observation.End.Milliseconds > now.Milliseconds ||
                now.Milliseconds - observation.End.Milliseconds > configuration.MaximumEvidenceAgeMs ||
                !Enum.IsDefined(observation.Continuity) ||
                observation.Confidence == EvidenceConfidence.Estimated &&
                (observation.End.Milliseconds - observation.Start.Milliseconds > EstimationPolicy.MaximumIntervalMs ||
                    now.Milliseconds - observation.End.Milliseconds > EstimationPolicy.MaximumAgeMs))
                return Reject("Invalid, overlapping, delayed or out-of-order interval.", now, before);

            var contiguous = observation.Start == boundary;
            if (!contiguous) InvalidateObservationContinuity();
            boundary = observation.End;
            lastObservation = observation;
            var activity = ApplicationClassifier.Classify(observation, configuration);
            if (activity == ActivityKind.Uncertain)
            {
                if (contiguous && !state.Paused && configuration.Accounting == AccountingMode.BestEffort &&
                    observation.Confidence == EvidenceConfidence.Unknown && observation.Session != SessionState.Suspended &&
                    observation.Continuity == ContinuityDisposition.PreserveTransient)
                    PreserveTransient(observation.End.Milliseconds - observation.Start.Milliseconds);
                else InvalidateObservationContinuity();
                return Result(activity, "Unverified evidence; no accounting.", [], state != before);
            }
            if (state.Paused)
            {
                ClearContinuity();
                return Result(activity, "Tracking paused.", [], state != before);
            }
            if (state.EffectSequence > long.MaxValue - 3)
            {
                state = state with { Recovery = RecoveryStatus.Required };
                return Result(ActivityKind.Uncertain, "Effect sequence exhausted; explicit recovery required.", [], false);
            }

            var effects = ImmutableArray.CreateBuilder<AdvisoryEffect>();
            var duration = observation.End.Milliseconds - observation.Start.Milliseconds;
            state = state with { ConsecutiveUncertaintyMs = 0 };
            // Snooze consumes verified time of every known classification, not gaps.
            var snoozedAtStart = state.Intervention == InterventionState.Snoozed ||
                state.Intervention == InterventionState.PersonalDeferred && activity == ActivityKind.CountedPersonal;
            var suppressedDuration = snoozedAtStart ? Math.Min(duration, state.SnoozeRemainingMs) : 0;
            if (snoozedAtStart)
            {
                var left = state.SnoozeRemainingMs - suppressedDuration;
                state = state with { SnoozeRemainingMs = left };
                if (left == 0) state = state with { Intervention = InterventionState.Nudging, ReminderRemainingMs = 0 };
            }

            var idleBefore = state.IdleProgressMs;
            state = state with
            {
                IdleProgressMs = observation.Input == InputState.Idle && observation.Session == SessionState.Active
                    ? AddCapped(idleBefore, duration, configuration.IdleThresholdMs) : 0
            };
            switch (activity)
            {
                case ActivityKind.CountedPersonal:
                    ClearBreak();
                    CountPersonal(duration, suppressedDuration, effects, interactive);
                    if (observation.Confidence == EvidenceConfidence.Estimated)
                        state = state with { EstimatedUsageMs = AddCapped(state.EstimatedUsageMs, duration, state.UsageMs) };
                    break;
                case ActivityKind.Work:
                case ActivityKind.Locked:
                    EarnBreak(duration, effects);
                    break;
                case ActivityKind.Idle:
                    var qualificationRemaining = observation.Input == InputState.QualifiedIdle ? 0 : configuration.IdleThresholdMs - idleBefore;
                    // A restored completed break needs no new credit. Keep its latch
                    // while idle is requalified so restart cannot repeat that reset.
                    if (qualificationRemaining > 0 && !state.BreakCompleted) ClearBreak();
                    if (duration > qualificationRemaining)
                        EarnBreak(duration - qualificationRemaining, effects);
                    break;
                default:
                    ClearBreak();
                    break;
            }
            return Result(activity, observation.Confidence == EvidenceConfidence.Estimated ? "Estimated interval accepted." :
                "Certified interval accepted.", effects.ToImmutable(), state != before);
        }
    }

    public TransitionResult SetPaused(bool paused)
    {
        lock (gate)
        {
            if (state.Recovery == RecoveryStatus.Required) return Result(ActivityKind.Uncertain, "Recovery required.", [], false);
            if (state.Paused == paused) return Result(ActivityKind.Uncertain, "Pause unchanged.", [], false);
            CommandBoundary();
            ClearContinuity();
            state = state with { Paused = paused };
            return Result(ActivityKind.Uncertain, paused ? "Tracking paused." : "Tracking resumed.", [], true);
        }
    }

    public TransitionResult ResetUsage()
    {
        lock (gate)
        {
            if (state.Recovery != RecoveryStatus.Ready)
                throw new InvalidOperationException("Usage reset requires ready accounting storage.");
            CommandBoundary();
            state = Fresh() with { Paused = state.Paused, EffectSequence = state.EffectSequence };
            return Result(ActivityKind.Uncertain, "Usage and break progress reset.", [], true);
        }
    }

    public TransitionResult Snooze()
    {
        lock (gate)
        {
            if (state.Recovery == RecoveryStatus.Required || state.Paused || !state.Exhausted)
                return Result(ActivityKind.Uncertain, "Snooze unavailable.", [], false);
            if (state.Intervention is InterventionState.Snoozed or InterventionState.PersonalDeferred)
                return Result(ActivityKind.Uncertain, "Already snoozed.", [], false);
            CommandBoundary();
            state = state with { Intervention = InterventionState.Snoozed, SnoozeRemainingMs = configuration.SnoozeMs, ReminderRemainingMs = 0 };
            return Result(ActivityKind.Uncertain, "Reminders snoozed; usage continues.", [], true);
        }
    }

    public TransitionResult ChangeConfiguration(MonitoringConfiguration replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        replacement.Validate();
        lock (gate)
        {
            if (state.Recovery == RecoveryStatus.Required) return Result(ActivityKind.Uncertain, "Recovery required.", [], false);
            if (state.EffectSequence > long.MaxValue - 3)
            {
                state = state with { Recovery = RecoveryStatus.Required };
                return Result(ActivityKind.Uncertain, "Effect sequence exhausted; explicit recovery required.", [], false);
            }
            var preserveCompleted = state.BreakCompleted &&
                replacement with { Accounting = configuration.Accounting } == configuration;
            CommandBoundary();
            ClearContinuity();
            if (preserveCompleted)
                state = state with { BreakCompleted = true, BreakProgressMs = replacement.RequiredBreakMs };
            // Duration/accounting edits must not shorten the shared app cadence
            // to the retained legacy field. Explicit legacy cadence edits still
            // use their original limit for low-level clients.
            var repeatLimit = replacement.RepeatNudgeMs == configuration.RepeatNudgeMs
                ? Math.Max(replacement.RepeatNudgeMs, replacement.InteractiveRepeatNudgeMs) : replacement.RepeatNudgeMs;
            configuration = replacement;
            var effects = ImmutableArray.CreateBuilder<AdvisoryEffect>();
            if (!state.Exhausted && state.UsageMs >= configuration.AllowanceMs) Exhaust(effects);
            else state = state with
            {
                ReminderRemainingMs = Math.Min(state.ReminderRemainingMs,
                    state.Intervention == InterventionState.Grace ? configuration.GraceMs : repeatLimit),
                SnoozeRemainingMs = state.Intervention == InterventionState.PersonalDeferred ? state.SnoozeRemainingMs :
                    Math.Min(state.SnoozeRemainingMs, configuration.SnoozeMs)
            };
            return Result(ActivityKind.Uncertain, "Configuration changed prospectively.", effects.ToImmutable(), true);
        }
    }

    // Unlike legacy Snooze, only counted Personal observations consume this remainder.
    public TransitionResult DeferInteractive(long durationMs)
    {
        if (durationMs is not (60_000 or 300_000)) throw new ArgumentException("Choose one or five minutes.");
        lock (gate)
        {
            if (state.Recovery != RecoveryStatus.Ready || state.Paused || !state.Exhausted)
                throw new InvalidOperationException("Extra time is unavailable.");
            if (state.Intervention is InterventionState.PersonalDeferred or InterventionState.Snoozed)
                return Result(ActivityKind.Uncertain, "Extra time is already active.", [], false);
            CommandBoundary();
            state = state with { Intervention = InterventionState.PersonalDeferred, SnoozeRemainingMs = durationMs, ReminderRemainingMs = 0 };
            return Result(ActivityKind.Uncertain, "Extra Personal time saved; usage continues.", [], true);
        }
    }

    public TransitionResult ChangeInteractiveRepeat(long durationMs)
    {
        lock (gate)
        {
            var replacement = configuration with { InteractiveRepeatNudgeMs = durationMs };
            replacement.Validate();
            if (state.Recovery != RecoveryStatus.Ready) throw new InvalidOperationException("Storage is unavailable.");
            if (replacement == configuration) return Result(ActivityKind.Uncertain, "Repeat interval unchanged.", [], false);
            // Cadence only: do not establish an accounting boundary or discard an unfinished break.
            configuration = replacement;
            if (state.Intervention == InterventionState.Nudging)
                state = state with { ReminderRemainingMs = Math.Min(state.ReminderRemainingMs, durationMs) };
            return Result(ActivityKind.Uncertain, "Reminder repeat interval saved.", [], true);
        }
    }

    private void CountPersonal(long duration, long suppressedDuration, ImmutableArray<AdvisoryEffect>.Builder effects, bool interactive)
    {
        var untilExhausted = state.Exhausted ? 0 : configuration.AllowanceMs - state.UsageMs;
        state = state with { UsageMs = AddCapped(state.UsageMs, duration, long.MaxValue) };
        if (!state.Exhausted && duration >= untilExhausted) Exhaust(effects);
        if (!state.Exhausted || state.Intervention is InterventionState.Snoozed or InterventionState.PersonalDeferred) return;
        var repeat = interactive ? configuration.InteractiveRepeatNudgeMs : configuration.RepeatNudgeMs;
        if (interactive) state = state with { ReminderRemainingMs = state.Intervention == InterventionState.Grace ? 0 : Math.Min(state.ReminderRemainingMs, repeat) };
        else if (state.Intervention == InterventionState.Nudging)
            state = state with { ReminderRemainingMs = Math.Min(state.ReminderRemainingMs, repeat) };
        var eligible = duration - Math.Max(untilExhausted, suppressedDuration);
        if (eligible < state.ReminderRemainingMs)
        {
            state = state with { ReminderRemainingMs = state.ReminderRemainingMs - eligible };
            return;
        }
        // At most one nudge per accepted interval: missed reminders are coalesced.
        Emit(EffectKind.Nudge, effects);
        state = state with { Intervention = InterventionState.Nudging, ReminderRemainingMs = repeat };
    }

    private void Exhaust(ImmutableArray<AdvisoryEffect>.Builder effects)
    {
        state = state with { Exhausted = true, Intervention = InterventionState.Grace, ReminderRemainingMs = configuration.GraceMs, SnoozeRemainingMs = 0 };
        Emit(EffectKind.AllowanceExhausted, effects);
    }

    private void EarnBreak(long duration, ImmutableArray<AdvisoryEffect>.Builder effects)
    {
        if (state.BreakCompleted) return;
        var progress = AddCapped(state.BreakProgressMs, duration, configuration.RequiredBreakMs);
        state = state with { BreakProgressMs = progress };
        if (progress != configuration.RequiredBreakMs) return;
        state = state with
        {
            UsageMs = 0,
            EstimatedUsageMs = 0,
            BreakUncertaintyMs = 0,
            ConsecutiveUncertaintyMs = 0,
            Exhausted = false,
            BreakCompleted = true,
            Intervention = InterventionState.Normal,
            ReminderRemainingMs = 0,
            SnoozeRemainingMs = 0
        };
        Emit(EffectKind.AllowanceReset, effects);
    }

    private bool AcceptClock(MonotonicTime now)
    {
        var accepted = ValidTime(now) && now.Epoch == lastReceipt.Epoch && now.Milliseconds >= lastReceipt.Milliseconds;
        if (ValidTime(now) && (now.Epoch != lastReceipt.Epoch || now.Milliseconds >= lastReceipt.Milliseconds))
            lastReceipt = now;
        return accepted;
    }

    private TransitionResult Reject(string reason, MonotonicTime now, EngineState before)
    {
        InvalidateObservationContinuity();
        lastObservation = null;
        if (ValidTime(now) && (now.Epoch != boundary.Epoch || now.Milliseconds >= boundary.Milliseconds)) boundary = now;
        return Result(ActivityKind.Uncertain, reason, [], state != before);
    }

    private void CommandBoundary()
    {
        var now = clock.Now;
        if (!ValidTime(now) || (now.Epoch == lastReceipt.Epoch && now.Milliseconds < lastReceipt.Milliseconds))
            throw new InvalidOperationException("Invalid or regressing monotonic clock.");
        // Commands account for no unsupplied interval, regardless of elapsed clock time.
        if (now != boundary) ClearContinuity();
        boundary = lastReceipt = now;
        lastObservation = null;
    }

    private void ClearBreak() => state = state with
    { BreakProgressMs = 0, BreakCompleted = false, BreakUncertaintyMs = 0, ConsecutiveUncertaintyMs = 0 };
    private void InvalidateObservationContinuity()
    {
        if (configuration.Accounting == AccountingMode.BestEffort && state.BreakCompleted)
            state = state with { IdleProgressMs = 0 };
        else ClearContinuity();
    }
    private void PreserveTransient(long duration)
    {
        if (state.BreakProgressMs == 0 || state.BreakCompleted) return;
        if (duration > EstimationPolicy.MaximumConsecutiveUncertaintyMs - state.ConsecutiveUncertaintyMs ||
            duration > EstimationPolicy.MaximumBreakUncertaintyMs - state.BreakUncertaintyMs)
        { InvalidateObservationContinuity(); return; }
        state = state with
        {
            BreakUncertaintyMs = state.BreakUncertaintyMs + duration,
            ConsecutiveUncertaintyMs = state.ConsecutiveUncertaintyMs + duration
        };
    }
    private void ClearContinuity()
    {
        ClearBreak();
        state = state with { IdleProgressMs = 0 };
    }
    private void Emit(EffectKind kind, ImmutableArray<AdvisoryEffect>.Builder effects)
    {
        state = state with { EffectSequence = checked(state.EffectSequence + 1) };
        effects.Add(new(state.EffectSequence, kind));
    }
    private TransitionResult Result(ActivityKind activity, string reason, ImmutableArray<AdvisoryEffect> effects, bool checkpoint) =>
        new(state, activity, reason, effects, checkpoint);
    private static EngineState Fresh() => new(0, false, 0, false, 0, false, InterventionState.Normal, 0, 0, 0, RecoveryStatus.Ready);
    private static bool ValidTime(MonotonicTime time) => time.Epoch != Guid.Empty && time.Milliseconds >= 0;
    private static long AddCapped(long current, long amount, long cap) => amount >= cap - current ? cap : current + amount;
}
