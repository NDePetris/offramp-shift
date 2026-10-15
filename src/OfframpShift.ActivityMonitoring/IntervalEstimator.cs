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
    private sealed record Pending(MonitoringObservation Observation, long Generation);
    private readonly Queue<Pending> pending = new();
    private EvidencePoint? previous;
    private ProviderHealth? previousHealth;
    private long idleCoverage, lastSequence, lastCommittedEnd, currentGeneration;
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
        var sameInput = point.IdleAgeMs is { } age && before.IdleAgeMs is { } priorAge &&
            point.UptimeMs - age == before.UptimeMs - priorAge;
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
            known &= point.Input == Capability.Available && before.Input == Capability.Available;
            if (scope == DesktopScope.Personal)
                known &= point.Foreground == Capability.Available && before.Foreground == Capability.Available &&
                    point.Application is { Verified: true } && point.Application == before.Application && point.Window == before.Window && point.Process == before.Process;
        }
        var start = before.Time;
        var end = point.Time;
        var idleBefore = idleCoverage;
        if (known && point.Session == SessionState.Active)
            idleCoverage = sameInput ? Math.Min(configuration.IdleThresholdMs, idleCoverage + duration) : Math.Min(configuration.IdleThresholdMs, Math.Min(duration, point.IdleAgeMs ?? 0));
        else if (hard) idleCoverage = 0;
        if (known && scope != DesktopScope.Work && point.Session == SessionState.Active && !sameInput && idleBefore >= configuration.IdleThresholdMs && configuration.IdleThresholdMs > 0)
        { known = false; hard = true; }
        if (known)
        {
            if (point.Session == SessionState.Active && scope != DesktopScope.Work && sameInput)
            {
                var untilQualified = Math.Min(duration, Math.Max(0, configuration.IdleThresholdMs - idleBefore));
                var eligibleActive = Math.Min(untilQualified, Math.Max(0, configuration.IdleThresholdMs - (before.IdleAgeMs ?? 0)));
                var activeEnd = start with { Milliseconds = start.Milliseconds + eligibleActive };
                var qualifiedStart = start with { Milliseconds = start.Milliseconds + untilQualified };
                Enqueue(Estimate(start, activeEnd, InputState.Active));
                Enqueue(new(activeEnd, qualifiedStart, DesktopScope.Unknown, SessionState.Unknown, null,
                    InputState.Unknown, MonitoringAvailability.Available, EvidenceConfidence.Unknown)
                { Continuity = ContinuityDisposition.PreserveTransient });
                Enqueue(Estimate(qualifiedStart, end, InputState.QualifiedIdle));
                Reason = untilQualified == duration && eligibleActive == 0 ? "IdleAwaitingMonitoredQualification" : "BoundedEstimate";
            }
            else
            {
                Enqueue(Estimate(start, end, point.Session == SessionState.Locked ? InputState.Unknown :
                    idleCoverage >= configuration.IdleThresholdMs ? InputState.QualifiedIdle : InputState.Active));
                Reason = "BoundedEstimate";
            }
        }
        else
        {
            Enqueue(new(start, end, DesktopScope.Unknown, SessionState.Unknown, null, InputState.Unknown,
                MonitoringAvailability.Unavailable, EvidenceConfidence.Unknown)
            { Continuity = hard || interrupted ? ContinuityDisposition.Invalidate : ContinuityDisposition.PreserveTransient });
            Reason = hard ? "Discontinuity" : interrupted ? "ObservedTransition" : "TransientUnavailable";
        }
        previous = point; previousHealth = health; interrupted = hardInterruption = false;
        if (pending.Count > 6)
        {
            var entries = pending.ToArray(); pending.Clear();
            pending.Enqueue(Invalidate(entries[0] with
            { Observation = entries[0].Observation with { End = entries[^1].Observation.End } }));
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
                o = Invalidate(first with { Observation = o with { End = now } }).Observation;
                Reset();
                Reason = "StaleGapFreshBaselineRequired";
                UnknownMs += o.End.Milliseconds - o.Start.Milliseconds;
                result.Add(o); lastCommittedEnd = o.End.Milliseconds; committedEpoch = o.End.Epoch;
                break;
            }
            if (now.Milliseconds - o.End.Milliseconds < EstimationPolicy.HoldbackMs) break;
            pending.Dequeue();
            if (!health.Healthy || first.Generation != health.Generation || previousHealth?.Generation != health.Generation ||
                now.Milliseconds - o.End.Milliseconds > EstimationPolicy.MaximumAgeMs ||
                previous is null || now.Milliseconds - previous.Time.Milliseconds + previous.CollectionMs > EstimationPolicy.MaximumAgeMs)
                o = Invalidate(first).Observation;
            if (o.Confidence == EvidenceConfidence.Estimated) EstimatedMs += o.End.Milliseconds - o.Start.Milliseconds;
            else UnknownMs += o.End.Milliseconds - o.Start.Milliseconds;
            result.Add(o); lastCommittedEnd = o.End.Milliseconds; committedEpoch = o.End.Epoch;
        }
        return result;
    }

    public void Reset()
    {
        pending.Clear(); previous = null; previousHealth = null; idleCoverage = 0;
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
    private void Enqueue(MonitoringObservation observation)
    { if (observation.End.Milliseconds > observation.Start.Milliseconds) pending.Enqueue(new(observation, currentGeneration)); }
    private Pending Invalidate(Pending entry)
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
        }, entry.Generation);
    }
}
