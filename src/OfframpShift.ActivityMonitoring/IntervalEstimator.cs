using OfframpShift.Core;
using WindowsObservation;

namespace OfframpShift.ActivityMonitoring;

public sealed record ProviderHealth(bool Healthy, long Generation);

// Serialized by the host. Snapshots support estimates, never certified history.
public sealed class IntervalEstimator
{
    private readonly Guid personal, work;
    private readonly MonitoringConfiguration configuration;
    public IntervalEstimator(Guid personal, Guid work, MonitoringConfiguration configuration)
    {
        if (personal == Guid.Empty || work == Guid.Empty || personal == work) throw new ArgumentException("Distinct desktop bindings required.");
        ArgumentNullException.ThrowIfNull(configuration); configuration.Validate();
        this.personal = personal; this.work = work; this.configuration = configuration;
    }
    private enum UnknownCause { None, EndpointDiscontinuity, ObservedTransition, UnavailableEndpoint, SignalInvalidation, PendingOverflow, StaleGap, FinalizationHealthOrFreshness }
    private sealed record Pending(MonitoringObservation Observation, long Generation, UnknownCause Cause);
    private readonly long[] unknownCounts = new long[Enum.GetValues<UnknownCause>().Length], unknownDurations = new long[Enum.GetValues<UnknownCause>().Length];
    private long rejectedDurationMs, discardedPendingIntervals;
    public object CoverageDiagnostics => new
    {
        unknown = Enum.GetValues<UnknownCause>().Where(x => x != UnknownCause.None)
            .ToDictionary(x => x.ToString(), x => new { count = unknownCounts[(int)x], durationMs = unknownDurations[(int)x] }),
        rejectedDurationMs,
        discardedPendingIntervals
    };
    private readonly Queue<Pending> pending = new();
    private EvidencePoint? previous;
    private ProviderHealth? previousHealth;
    private long lastSequence, lastCommittedEnd, currentGeneration;
    private Guid committedEpoch;
    public long EstimatedMs { get; private set; }
    public long UnknownMs { get; private set; }
    public long LateContradictions { get; private set; }
    public long InvalidatedIntervals { get; private set; }
    public long CoreRejectedIntervals { get; private set; }
    public long CoreAcceptedEstimatedMs { get; private set; }
    public long CoreAcceptedUnknownMs { get; private set; }
    public string CoreLastReason { get; private set; } = "AwaitingDelivery";
    public string Reason { get; private set; } = "AwaitingBaseline";

    // Called only after Observe returns successfully (after any required durable Save).
    // Keep Core's existing result contract; regressions exercise these rejection reasons.
    // False means discard the rest of the finalized batch as well as queued history.
    public bool AcceptDelivery(MonitoringObservation observation, TransitionResult result)
    {
        CoreLastReason = result.Reason;
        if (result.Reason is "Invalid, overlapping, delayed or out-of-order interval." or "Monotonic clock discontinuity.")
        {
            CoreRejectedIntervals++;
            rejectedDurationMs += observation.End.Milliseconds - observation.Start.Milliseconds;
            Reset();
            Reason = "CoreRejectedFreshBaselineRequired";
            return false;
        }
        if (result.Reason == "Estimated interval accepted.")
            CoreAcceptedEstimatedMs += observation.End.Milliseconds - observation.Start.Milliseconds;
        else if (result.Reason == "Unverified evidence; no accounting." && observation.Confidence == EvidenceConfidence.Unknown)
            CoreAcceptedUnknownMs += observation.End.Milliseconds - observation.Start.Milliseconds;
        return true;
    }

    public void Signal(NativeSignal signal)
    {
        var fault = signal.Sequence != lastSequence + 1 || previous is { } current && current.Time.Epoch != signal.Receipt.Epoch;
        if (signal.Sequence <= lastSequence) fault = true;
        lastSequence = Math.Max(lastSequence, signal.Sequence);
        if (signal.Kind == SignalKind.Tick && !fault) return;
        if (signal.Kind == SignalKind.Destroyed && !fault && previous is { } p && signal.Window != p.Window) return;
        var generated = signal.GeneratedTick is { } tick ? TickMath.Age(tick, signal.ReceiptUptimeMs) : null;
        var time = generated is { } age && age <= signal.Receipt.Milliseconds
            ? signal.Receipt.Milliseconds - age : signal.Receipt.Milliseconds;
        var material = fault || signal.GeneratedTick is not null &&
            (generated is null || generated > signal.Receipt.Milliseconds) ||
            signal.Kind is SignalKind.Session or SignalKind.Power or SignalKind.PumpStall or SignalKind.Stopped;
        if (committedEpoch == signal.Receipt.Epoch && time < lastCommittedEnd)
        { LateContradictions++; material = true; }
        // No mutable Core state or DB work is performed by event processing.
        var entries = pending.ToArray(); pending.Clear();
        foreach (var entry in entries)
        {
            var observation = entry.Observation;
            var relevant = material || time >= observation.Start.Milliseconds && time <= observation.End.Milliseconds;
            if (!material && signal.Kind == SignalKind.Foreground &&
                observation.Desktop == DesktopScope.Work && observation.Session == SessionState.Active) relevant = false;
            pending.Enqueue(relevant ? Invalidate(entry) : entry);
        }
        if (material || signal.Kind != SignalKind.Foreground || previous is not { } prior || Scope(prior) != DesktopScope.Work)
        {
            // The next snapshot interval is also suspect, even if matching endpoints return.
            interrupted = true;
            hardInterruption |= material || signal.Kind == SignalKind.InputDesktopChanged;
        }
    }

    private bool interrupted, hardInterruption;
    public void Point(EvidencePoint point, ProviderHealth health)
    {
        if (point.Time.Epoch == Guid.Empty || point.Time.Milliseconds < 0) { Reset(); Reason = "InvalidClock"; return; }
        if (previous is not { } before || before.Time.Epoch != point.Time.Epoch)
        {
            Reset(); previous = point; previousHealth = health; Reason = "FreshBaseline"; return;
        }
        if (point.Time.Milliseconds <= before.Time.Milliseconds)
        {
            var entries = pending.ToArray(); pending.Clear();
            foreach (var entry in entries) pending.Enqueue(Invalidate(entry));
            interrupted = hardInterruption = true; Reason = "ClockRollback"; return;
        }
        var duration = point.Time.Milliseconds - before.Time.Milliseconds;
        currentGeneration = health.Generation;
        var scope = Scope(point);
        var priorScope = Scope(before);
        var hard = hardInterruption || health.Generation != previousHealth!.Generation ||
            duration > EstimationPolicy.MaximumIntervalMs || point.Desktop.ShellProcess != before.Desktop.ShellProcess ||
            point.Session == SessionState.Suspended || point.Session != before.Session ||
            scope != priorScope && scope != DesktopScope.Unknown && priorScope != DesktopScope.Unknown;
        if (point.Awake100ns is { } awake && before.Awake100ns is { } priorAwake &&
            (awake < priorAwake || duration - (long)Math.Min((awake - priorAwake) / 10_000, (ulong)long.MaxValue) > 250)) hard = true;
        var inputRegressed = point.IdleAgeMs is { } a && before.IdleAgeMs is { } b &&
            point.UptimeMs - a < before.UptimeMs - b;
        hard |= inputRegressed;
        hard |= point.InputDiscontinuity;
        var known = health.Healthy && previousHealth!.Healthy && !hard && !interrupted &&
            point.CollectionMs is >= 0 and <= 500 && before.CollectionMs is >= 0 and <= 500 &&
            point.Awake100ns is not null && before.Awake100ns is not null &&
            scope != DesktopScope.Unknown && scope == priorScope && point.SessionHealth == Capability.Available &&
            before.SessionHealth == Capability.Available && point.Session is SessionState.Active or SessionState.Locked;
        if (point.Session == SessionState.Active)
        {
            known &= point.Input == Capability.Available && before.Input == Capability.Available &&
                point.IdleAgeMs is >= 0 && before.IdleAgeMs is >= 0;
            if (scope == DesktopScope.Personal)
                known &= point.Foreground == Capability.Available && before.Foreground == Capability.Available &&
                    point.Application is { Verified: true } && point.Application == before.Application && point.Window == before.Window && point.Process == before.Process;
        }
        var start = before.Time;
        var end = point.Time;
        if (known)
        {
            // Inactivity describes input for consumers; it never splits accounting.
            // Only the fresh observed interval is estimated, including idle startup.
            Enqueue(Estimate(start, end, point.Session == SessionState.Locked ? InputState.Unknown :
                point.IdleAgeMs >= configuration.IdleThresholdMs ? InputState.QualifiedIdle : InputState.Active));
            Reason = "BoundedEstimate";
        }
        else
        {
            Enqueue(new(start, end, DesktopScope.Unknown, SessionState.Unknown, null, InputState.Unknown,
                MonitoringAvailability.Unavailable, EvidenceConfidence.Unknown)
            { Continuity = hard || interrupted ? ContinuityDisposition.Invalidate : ContinuityDisposition.PreserveTransient },
                hard ? UnknownCause.EndpointDiscontinuity : interrupted ? UnknownCause.ObservedTransition : UnknownCause.UnavailableEndpoint);
            Reason = hard ? "Discontinuity" : interrupted ? "ObservedTransition" : "TransientUnavailable";
        }
        previous = point; previousHealth = health; interrupted = hardInterruption = false;
        if (pending.Count > 6)
        {
            var entries = pending.ToArray(); pending.Clear();
            pending.Enqueue(Invalidate(entries[0] with
            { Observation = entries[0].Observation with { End = entries[^1].Observation.End } }, UnknownCause.PendingOverflow));
        }

        MonitoringObservation Estimate(MonotonicTime s, MonotonicTime e, InputState input) =>
            new(s, e, scope, point.Session, point.Application, input, MonitoringAvailability.Available, EvidenceConfidence.Estimated);
    }

    public IReadOnlyList<MonitoringObservation> Finalize(MonotonicTime now, ProviderHealth health)
    {
        var result = new List<MonitoringObservation>();
        while (pending.TryPeek(out var first))
        {
            var o = first.Observation;
            if (o.End.Epoch != now.Epoch) { Reset(); break; }
            if (now.Milliseconds - o.End.Milliseconds > Math.Min(EstimationPolicy.MaximumAgeMs, configuration.MaximumEvidenceAgeMs))
            {
                // Deliver the gap at a fresh receipt boundary, never an old estimate.
                // Core rejects stale endings and advances its boundary to receipt time;
                // replaying the held-back queue afterwards would perpetuate overlap.
                o = Invalidate(first with { Observation = o with { End = now } }, UnknownCause.StaleGap).Observation;
                Reset();
                Reason = "StaleGapFreshBaselineRequired";
                UnknownMs += o.End.Milliseconds - o.Start.Milliseconds;
                RecordUnknown(UnknownCause.StaleGap, o);
                result.Add(o); lastCommittedEnd = o.End.Milliseconds; committedEpoch = o.End.Epoch;
                break;
            }
            if (now.Milliseconds - o.End.Milliseconds < EstimationPolicy.HoldbackMs) break;
            pending.Dequeue();
            if (!health.Healthy || first.Generation != health.Generation || previousHealth?.Generation != health.Generation ||
                now.Milliseconds - o.End.Milliseconds > EstimationPolicy.MaximumAgeMs ||
                previous is null || now.Milliseconds - previous.Time.Milliseconds + previous.CollectionMs > EstimationPolicy.MaximumAgeMs)
            {
                first = Invalidate(first, UnknownCause.FinalizationHealthOrFreshness);
                o = first.Observation;
            }
            if (o.Confidence == EvidenceConfidence.Estimated) EstimatedMs += o.End.Milliseconds - o.Start.Milliseconds;
            else { UnknownMs += o.End.Milliseconds - o.Start.Milliseconds; RecordUnknown(first.Cause, o); }
            result.Add(o); lastCommittedEnd = o.End.Milliseconds; committedEpoch = o.End.Epoch;
        }
        return result;
    }

    public void Reset()
    {
        discardedPendingIntervals += pending.Count;
        pending.Clear(); previous = null; previousHealth = null;
        interrupted = hardInterruption = false;
        Reason = "AwaitingBaseline";
    }
    public void ResetSignals() { Reset(); lastSequence = 0; }
    private DesktopScope Scope(EvidencePoint point) => point.Desktop.Health == Capability.Available &&
        point.Desktop.Current is { } current && current != Guid.Empty && point.Desktop.Desktops.Contains(current) &&
        point.Desktop.ShellProcess != 0 &&
        point.Desktop.Desktops.Contains(personal) && point.Desktop.Desktops.Contains(work)
        ? point.Desktop.Current == personal ? DesktopScope.Personal : point.Desktop.Current == work ? DesktopScope.Work : DesktopScope.Other
        : DesktopScope.Unknown;
    private void RecordUnknown(UnknownCause cause, MonitoringObservation observation)
    { unknownCounts[(int)cause]++; unknownDurations[(int)cause] += observation.End.Milliseconds - observation.Start.Milliseconds; }
    private void Enqueue(MonitoringObservation observation, UnknownCause cause = UnknownCause.None)
    { if (observation.End.Milliseconds > observation.Start.Milliseconds) pending.Enqueue(new(observation, currentGeneration, cause)); }
    private Pending Invalidate(Pending entry, UnknownCause cause = UnknownCause.SignalInvalidation)
    {
        InvalidatedIntervals++;
        return new(entry.Observation with
        {
            Desktop = DesktopScope.Unknown,
            Session = SessionState.Unknown,
            Application = null,
            Input = InputState.Unknown,
            Availability = MonitoringAvailability.Unavailable,
            Confidence = EvidenceConfidence.Unknown,
            Continuity = ContinuityDisposition.Invalidate
        }, entry.Generation, cause);
    }
}
