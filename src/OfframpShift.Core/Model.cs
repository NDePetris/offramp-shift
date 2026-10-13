using System.Collections.Immutable;

namespace OfframpShift.Core;

public enum DesktopScope { Unknown, Personal, Work, Other }
public enum SessionState { Unknown, Active, Locked, Suspended }
public enum InputState { Unknown, Active, Idle }
public enum MonitoringAvailability { Unknown, Available, Unavailable, Unsupported }
public enum EvidenceConfidence { Unknown, Confirmed, Conflicting }
public enum ClassificationMode { CountByDefault, BlocklistOnly }
public enum ActivityKind { Uncertain, CountedPersonal, Neutral, Work, Idle, Locked }
public enum InterventionState { Normal, Grace, Nudging, Snoozed }
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

// This certifies the entire half-open [Start, End) interval, not just its endpoint.
// An OS event or two equal snapshots alone cannot certify continuity.
public sealed record MonitoringObservation(
    MonotonicTime Start, MonotonicTime End,
    DesktopScope Desktop, SessionState Session, ApplicationIdentity? Application,
    InputState Input, MonitoringAvailability Availability, EvidenceConfidence Confidence);

public sealed record MonitoringConfiguration
{
    public long AllowanceMs { get; init; } = 30 * 60_000;
    public long RequiredBreakMs { get; init; } = 15 * 60_000;
    public long IdleThresholdMs { get; init; } = 60_000;
    public long GraceMs { get; init; } = 60_000;
    public long RepeatNudgeMs { get; init; } = 10 * 60_000;
    public long SnoozeMs { get; init; } = 10 * 60_000;
    // Receipt freshness is independent of certified interval duration.
    public long MaximumEvidenceAgeMs { get; init; } = 5_000;
    public ClassificationMode Mode { get; init; } = ClassificationMode.CountByDefault;
    // Excluded in CountByDefault; included in BlocklistOnly. Exact ordinal keys.
    public ImmutableArray<string> Applications { get; init; } = [];

    public void Validate()
    {
        if (AllowanceMs <= 0 || RequiredBreakMs <= 0 || IdleThresholdMs < 0 || GraceMs < 0 ||
            RepeatNudgeMs <= 0 || SnoozeMs <= 0 || MaximumEvidenceAgeMs <= 0 || !Enum.IsDefined(Mode))
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
            observation.Confidence != EvidenceConfidence.Confirmed || observation.Desktop == DesktopScope.Unknown)
            return ActivityKind.Uncertain;
        if (observation.Session == SessionState.Locked) return ActivityKind.Locked;
        if (observation.Session != SessionState.Active || observation.Input == InputState.Unknown)
            return ActivityKind.Uncertain;
        if (observation.Desktop == DesktopScope.Work) return ActivityKind.Work;
        if (observation.Input == InputState.Idle) return ActivityKind.Idle;
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
    RecoveryStatus Recovery);
public sealed record AdvisoryEffect(long Sequence, EffectKind Kind);
public sealed record TransitionResult(
    EngineState State, ActivityKind Activity, string Reason,
    ImmutableArray<AdvisoryEffect> Effects, bool CheckpointRequired);
