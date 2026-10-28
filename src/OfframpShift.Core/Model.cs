using System.Collections.Immutable;

namespace OfframpShift.Core;

public enum DesktopScope { Unknown, Personal, Work, Other }
public enum SessionState { Unknown, Active, Locked, Suspended }
public enum InputState { Unknown, Active, Idle, QualifiedIdle }
public enum MonitoringAvailability { Unknown, Available, Unavailable, Unsupported }
public enum EvidenceConfidence { Unknown, Confirmed, Conflicting, Estimated }
public enum AccountingMode { CertifiedOnly, BestEffort }
public enum ContinuityDisposition { Invalidate, PreserveTransient }
public enum ClassificationMode { CountByDefault, BlocklistOnly }
public enum ActivityKind { Uncertain, CountedPersonal, Neutral, Work, Idle, Locked }
public enum InterventionState { Normal, Grace, Nudging, Snoozed, PersonalDeferred }
public enum EffectKind { AllowanceExhausted, Nudge, AllowanceReset }
public enum RecoveryStatus { Ready, Required }

// Epoch is process-local. Neither it nor observation/application history is checkpointed.
public readonly record struct MonotonicTime(Guid Epoch, long Milliseconds);
public interface IMonitoringClock
{
    MonotonicTime Now { get; }
    DateTimeOffset UtcNow { get; }
}

// The adapter owns identity normalization and verification. No title, PID, or HWND.
public sealed record ApplicationIdentity(string Key, bool Verified);

// Confirmed certifies [Start, End); Estimated explicitly describes a bounded estimate.
// An OS event or matching snapshots cannot establish certification.
public sealed record MonitoringObservation(
    MonotonicTime Start, MonotonicTime End,
    DesktopScope Desktop, SessionState Session, ApplicationIdentity? Application,
    InputState Input, MonitoringAvailability Availability, EvidenceConfidence Confidence)
{
    public ContinuityDisposition Continuity { get; init; }
}

public static class EstimationPolicy
{
    public const int Version = 1;
    public const long MaximumIntervalMs = 1_500;
    public const long MaximumAgeMs = 2_000;
    public const long HoldbackMs = 1_000;
    public const long MaximumConsecutiveUncertaintyMs = 5_000;
    public const long MaximumBreakUncertaintyMs = 15_000;
}

public sealed record MonitoringConfiguration
{
    public long AllowanceMs { get; init; } = 30 * 60_000;
    public long RequiredBreakMs { get; init; } = 15 * 60_000;
    // Reminder presentation inactivity threshold; no usage or break qualification.
    public long IdleThresholdMs { get; init; } = 60_000;
    public long GraceMs { get; init; } = 60_000;
    public long RepeatNudgeMs { get; init; } = 10 * 60_000;
    public long SnoozeMs { get; init; } = 10 * 60_000;
    // Shared app reminder interval. Keep this v3 serialized name for compatibility.
    public long InteractiveRepeatNudgeMs { get; init; } = 60_000;
    // Receipt freshness is independent of certified interval duration.
    public long MaximumEvidenceAgeMs { get; init; } = 5_000;
    public AccountingMode Accounting { get; init; }
    public int EstimationPolicyVersion { get; init; } = EstimationPolicy.Version;
    public ClassificationMode Mode { get; init; } = ClassificationMode.CountByDefault;
    // Excluded in CountByDefault; included in BlocklistOnly. Exact ordinal keys.
    public ImmutableArray<string> Applications { get; init; } = [];

    public void Validate()
    {
        if (AllowanceMs <= 0 || RequiredBreakMs <= 0 || IdleThresholdMs < 0 || GraceMs < 0 ||
            RepeatNudgeMs <= 0 || SnoozeMs <= 0 || InteractiveRepeatNudgeMs is < 60_000 or > 3_600_000 ||
            InteractiveRepeatNudgeMs % 60_000 != 0 || MaximumEvidenceAgeMs <= 0 || !Enum.IsDefined(Mode) ||
            !Enum.IsDefined(Accounting) || EstimationPolicyVersion != EstimationPolicy.Version)
            throw new ArgumentException("Invalid monitoring configuration.");
        if (Applications.IsDefault || Applications.Any(string.IsNullOrWhiteSpace) ||
            Applications.Distinct(StringComparer.Ordinal).Count() != Applications.Length)
            throw new ArgumentException("Application keys must be nonempty and unique.");
    }
}

public static class ApplicationClassifier
{
    public static ActivityKind Classify(MonitoringObservation observation, MonitoringConfiguration configuration)
    {
        if (!Enum.IsDefined(observation.Desktop) || !Enum.IsDefined(observation.Session) ||
            !Enum.IsDefined(observation.Input) || observation.Availability != MonitoringAvailability.Available ||
            (observation.Confidence != EvidenceConfidence.Confirmed &&
                !(observation.Confidence == EvidenceConfidence.Estimated && configuration.Accounting == AccountingMode.BestEffort)) ||
            observation.Desktop == DesktopScope.Unknown)
            return ActivityKind.Uncertain;
        if (observation.Session == SessionState.Locked) return ActivityKind.Locked;
        if (observation.Session != SessionState.Active || observation.Input == InputState.Unknown)
            return ActivityKind.Uncertain;
        if (observation.Desktop == DesktopScope.Work) return ActivityKind.Work;
        if (observation.Desktop == DesktopScope.Other) return ActivityKind.Neutral;
        if (observation.Application is not { Verified: true } app || string.IsNullOrWhiteSpace(app.Key))
            return ActivityKind.Uncertain;
        var listed = configuration.Applications.Contains(app.Key, StringComparer.Ordinal);
        var counted = configuration.Mode == ClassificationMode.CountByDefault ? !listed : listed;
        return counted ? ActivityKind.CountedPersonal : ActivityKind.Neutral;
    }
}

public sealed record EngineState(
    long UsageMs, bool Exhausted, long BreakProgressMs, bool BreakCompleted,
    long IdleProgressMs, bool Paused, InterventionState Intervention,
    long ReminderRemainingMs, long SnoozeRemainingMs, long EffectSequence,
    RecoveryStatus Recovery)
{
    public DateTimeOffset? PauseUntilUtc { get; init; }
    public long EstimatedUsageMs { get; init; }
    public long BreakUncertaintyMs { get; init; }
    public long ConsecutiveUncertaintyMs { get; init; }
}
public sealed record AdvisoryEffect(long Sequence, EffectKind Kind);
public sealed record TransitionResult(
    EngineState State, ActivityKind Activity, string Reason,
    ImmutableArray<AdvisoryEffect> Effects, bool CheckpointRequired);
