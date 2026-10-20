using OfframpShift.Core;

namespace WindowsObservation;

public enum Capability { Available, Unavailable, Unsupported }
public enum NudgeDesktopCheck
{
    Allowed, ForegroundChanged, SessionUnavailable, PersonalDesktopChanged, CandidateNotCurrent,
    CandidateIdentityUnavailable, CandidateWrongDesktop, ForegroundIdentityUnavailable,
    ForegroundWrongDesktop, QueryUnavailable, WorkerUnavailable
}
public enum ForegroundFailure
{
    None, NoWindowOrProcess, ProcessQueryUnavailable, ProcessTimesUnavailable,
    ProcessImageUnavailable, GenericHostUnsupported, ApplicationIdentityQueryUnavailable,
    PackagedIdentityUnavailable, ProcessExitedOrForegroundChanged,
    IdentityCorroborationFailed, ForegroundChangedDuringCollection
}
public enum SignalKind { Foreground, Destroyed, InputDesktopChanged, Session, Power, Tick, PumpStall, Stopped }
public sealed record NativeSignal(long Sequence, SignalKind Kind, MonotonicTime Receipt,
    long ReceiptUptimeMs, uint? GeneratedTick, nint Window = 0, uint Detail = 0);
public sealed record DesktopReading(Capability Health, Guid? Current, Guid[] Desktops, uint ShellProcess);
public sealed record EvidencePoint(MonotonicTime Time, long UptimeMs, ulong? Awake100ns,
    nint Window, uint Process, ApplicationIdentity? Application, long? IdleAgeMs,
    SessionState Session, DesktopReading Desktop, Capability Foreground, Capability Input, Capability SessionHealth)
{
    public long CollectionMs { get; init; }
    public bool InputDiscontinuity { get; init; }
    public ForegroundFailure ForegroundFailure { get; init; }
    public int? ForegroundIdentityQueryCode { get; init; }
}
public sealed record EvidenceResult(MonitoringObservation? Interval, string Reason);

public static class DesktopObservationPolicy
{
    // Owner-approved exact 26200 builds and controlled 26300-family evaluation.
    // This is independent of switching capability and consent.
    private static bool Is26300Family(string build) => build?.Split('.') is ["26300", var revision] &&
        uint.TryParse(revision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);
    public static bool IsSupported(string build, string architecture) =>
        (build is "26200.9457" or "26200.9711" || Is26300Family(build)) && architecture == "X64";
    public static bool SameConsentScope(string savedBuild, string currentBuild) =>
        savedBuild == currentBuild || Is26300Family(savedBuild) && Is26300Family(currentBuild);
}

// Portable cleanup decision, with native process operations supplied by the owner.
public static class WorkerShutdown
{
    public static void Stop(Func<bool> exited, Action kill, Func<bool> wait)
    {
        if (exited()) return;
        try { kill(); }
        catch (InvalidOperationException) when (exited()) { } // Exit raced our kill.
        if (!wait()) throw new TimeoutException("WorkerShutdownTimeout");
    }
}

// Native evidence does not establish complete cross-provider interval coverage.
// There is deliberately no flag, CLI option or heuristic that can turn this into Confirmed.
public sealed class EvidenceInvestigation
{
    public const long MaximumReconciliationGapMs = 2_000;
    private EvidencePoint? previous;
    private long lastSequence;
    private MonotonicTime? lastSignalTime;
    public long Signals { get; private set; }
    public long OrderingFaults { get; private set; }
    public long DelayedSignals { get; private set; }
    public long MaximumDeliveryAgeMs { get; private set; }
    public long UncertainMs { get; private set; }
    public long CertifiedMs => 0;

    public string AcceptSignal(NativeSignal signal)
    {
        if (signal.Sequence <= lastSequence)
        {
            OrderingFaults++;
            return "DuplicateOrReorderedSignal";
        }
        var gap = signal.Sequence != lastSequence + 1;
        lastSequence = signal.Sequence;
        Signals++;
        if (gap) { OrderingFaults++; return "SignalGap"; }
        if (lastSignalTime is { } last && last.Epoch == signal.Receipt.Epoch &&
            signal.Receipt.Milliseconds < last.Milliseconds)
        {
            OrderingFaults++;
            return "ReceiptRollback";
        }
        lastSignalTime = signal.Receipt;
        if (signal.GeneratedTick is { } generated && TickMath.Age(generated, signal.ReceiptUptimeMs) is { } age)
        {
            MaximumDeliveryAgeMs = Math.Max(MaximumDeliveryAgeMs, age);
            if (age > MaximumReconciliationGapMs) { DelayedSignals++; return "DelayedNativeEvent"; }
        }
        return "SignalIsNotCoverage";
    }

    public EvidenceResult AcceptPoint(EvidencePoint point)
    {
        if (point.Time.Epoch == Guid.Empty || point.Time.Milliseconds < 0) return new(null, "InvalidClock");
        if (previous is not { } prior || prior.Time.Epoch != point.Time.Epoch)
        {
            previous = point;
            return new(null, "FreshBaseline");
        }
        // Preserve the high-water boundary on same-epoch rollback. No overlapping output.
        if (point.Time.Milliseconds <= prior.Time.Milliseconds) return new(null, "ClockRollbackOrDuplicate");
        previous = point;
        var duration = point.Time.Milliseconds - prior.Time.Milliseconds;
        UncertainMs = duration > long.MaxValue - UncertainMs ? long.MaxValue : UncertainMs + duration;
        var reason = duration > MaximumReconciliationGapMs ? "ReconciliationGap" :
            point.Desktop.ShellProcess != prior.Desktop.ShellProcess ? "ShellGenerationChanged" :
            point.Window != prior.Window || point.Desktop.Current != prior.Desktop.Current ||
            point.Session != prior.Session ? "EndpointChanged" : "MatchingEndpointsAreNotCoverage";
        if (point.Awake100ns is { } awake && prior.Awake100ns is { } before &&
            (awake < before || duration - (long)Math.Min((awake - before) / 10_000, (ulong)long.MaxValue) > 250))
            reason = "PossibleSuspendOrClockDiscontinuity";
        // Endpoint state is kept in EvidencePoint, never asserted over the whole interval.
        return new(new(prior.Time, point.Time, DesktopScope.Unknown, SessionState.Unknown, null,
            InputState.Unknown, MonitoringAvailability.Unavailable, EvidenceConfidence.Unknown), reason);
    }
}

public static class TickMath
{
    // Reject half-wrap ambiguity and future/inconsistent ticks rather than inventing an epoch.
    public static long? Age(uint tick, long uptime)
    {
        if (uptime < 0) return null;
        var age = unchecked((uint)uptime - tick);
        return age <= int.MaxValue && age <= uptime ? age : null;
    }
}

public sealed class LastInputTracker
{
    private long? previousTick;
    public bool LastReadDiscontinuous { get; private set; }
    public long? Read(uint tick, long uptime)
    {
        var age = TickMath.Age(tick, uptime);
        LastReadDiscontinuous = false;
        if (age is null) { previousTick = null; LastReadDiscontinuous = true; return null; }
        var absolute = uptime - age.Value;
        if (previousTick is { } previous && absolute < previous) { previousTick = null; LastReadDiscontinuous = true; return null; }
        previousTick = absolute;
        return age;
    }
}
