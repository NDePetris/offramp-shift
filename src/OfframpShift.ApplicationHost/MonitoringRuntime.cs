using System.Diagnostics;
using OfframpShift.ActivityMonitoring;
using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;
using ActivityKind = OfframpShift.Core.ActivityKind;

namespace OfframpShift.ApplicationHost;

// One owner calls every method. This is the PR 4 scheduling/delivery loop shared
// by the app and probe, with timing and native access supplied at the boundary.
public sealed class MonitoringRuntime : IDisposable
{
    private readonly IMonitoringClock clock;
    private readonly Action newEpoch;
    private readonly Func<long> elapsed;
    private readonly Func<IObservationSource> factory;
    private readonly DurableMonitoringSession session;
    private readonly Guid personal, work;
    private IObservationSource source;
    private IntervalEstimator estimator;
    private MonitoringConfiguration configuration;
    private long next, lastTick = -1, generation, drops, errors;
    private long lastRecovery = -10_000, eventSampleAt = long.MaxValue, lastSampleAt = -100;
    private uint shellGeneration, helperShellGeneration;
    private readonly LatencyHistogram cycles = new(), scheduleDelay = new(), activeCycles = new(), idleCycles = new(), lockedCycles = new();
    private long slowCycles, maximumDeliveryAgeMs, maximumIncompleteBreakMs, incompleteBreakDrops;
    private long personalBreakDrops, neutralBreakDrops, uncertainBreakDrops, otherBreakDrops, invalidatingUnknownBreakDrops, transientUnknownBreakDrops;
    private long foregroundSignals, destroyedSignals, desktopSignals, sessionSignals, suspendResumeSignals, otherPowerSignals;
    private readonly Dictionary<ForegroundFailure, long> foregroundFailures = new();
    private ForegroundFailure lastForegroundFailure;
    private int? lastForegroundIdentityQueryCode;
    public EvidencePoint? Latest { get; private set; }
    public ProviderHealth Health { get; private set; } = new(false, 0);
    public string Reason => estimator.Reason;
    public long RetryInMs => Math.Max(0, 10_000 - (elapsed() - lastRecovery));
    public long Samples { get; private set; }
    public long MissedDeadlines { get; private set; }
    public long Recoveries { get; private set; }
    public long EffectsCommitted { get; private set; }
    public long EffectsSuppressed { get; private set; }
    public long QueueHigh { get; private set; }

    public MonitoringRuntime(IMonitoringClock clock, Action newEpoch, Func<long> elapsed,
        Func<IObservationSource> factory, DurableMonitoringSession session, Guid personal, Guid work)
    {
        this.clock = clock; this.newEpoch = newEpoch; this.elapsed = elapsed;
        this.factory = factory; this.session = session; this.personal = personal; this.work = work;
        configuration = session.Configuration;
        estimator = new(personal, work, configuration);
        source = factory();
        try { shellGeneration = helperShellGeneration = source.ShellGeneration; }
        catch { source.Dispose(); throw; }
    }

    public void ResetConfiguration()
    {
        // Retain the native stream sequence when providers have not changed.
        estimator.Reset();
        configuration = session.Configuration;
        estimator = new(personal, work, configuration);
        // Drain prior signals into the new estimator; sequence mismatch fails closed.
        Latest = null; Health = new(false, ++generation);
    }

    public bool RetryProviders()
    {
        if (RetryInMs != 0) return false;
        lastRecovery = elapsed(); Recoveries++;
        // Failed cleanup must not create a second provider bundle.
        source.Dispose();
        source = factory();
        estimator.ResetSignals(); Latest = null; Health = new(false, ++generation);
        lastTick = -1; drops = errors = 0;
        helperShellGeneration = shellGeneration = source.ShellGeneration;
        return true;
    }

    public EvidencePoint CaptureCurrent()
    {
        estimator.Reset();
        Latest = source.Read();
        Drain(); RefreshHealth();
        return Latest;
    }

    public void Step(bool accounting = true)
    {
        Drain();
        if (elapsed() >= next || elapsed() >= eventSampleAt && elapsed() - lastSampleAt >= 100)
        {
            var periodic = elapsed() >= next;
            var lateness = periodic ? elapsed() - next : 0;
            scheduleDelay.Record(lateness * 1_000);
            if (lateness >= 1_000) MissedDeadlines += lateness / 1_000;
            if (periodic) next = (elapsed() / 1_000 + 1) * 1_000;
            lastSampleAt = elapsed(); eventSampleAt = long.MaxValue;
            var started = Stopwatch.GetTimestamp();
            var shell = source.ShellGeneration;
            if (shellGeneration != 0 && shell != shellGeneration) generation++;
            if (shell != 0 && shell != helperShellGeneration && elapsed() - lastRecovery >= 10_000)
            {
                // Preserve PR 4: one attempt per new shell, no recovery loop.
                lastRecovery = elapsed(); Recoveries++; helperShellGeneration = shell;
                source.ReplaceDesktopWorker();
            }
            shellGeneration = shell;
            Latest = source.Read(); Samples++;
            if (Latest.Foreground == Capability.Unavailable)
            {
                foregroundFailures.TryGetValue(Latest.ForegroundFailure, out var count);
                foregroundFailures[Latest.ForegroundFailure] = count + 1;
                lastForegroundFailure = Latest.ForegroundFailure; lastForegroundIdentityQueryCode = Latest.ForegroundIdentityQueryCode;
            }
            Drain(); RefreshHealth();
            if (accounting && session.Status == PersistenceStatus.Ready && !session.State.Paused)
                estimator.Point(Latest, Health);
            else estimator.Reset();
            var cycleUs = Stopwatch.GetElapsedTime(started).Ticks / 10;
            cycles.Record(cycleUs); if (cycleUs > 500_000) slowCycles++;
            if (Latest.Session == SessionState.Locked) lockedCycles.Record(cycleUs);
            else if (Latest.IdleAgeMs >= configuration.IdleThresholdMs) idleCycles.Record(cycleUs);
            else activeCycles.Record(cycleUs);
        }
        Drain(); RefreshHealth();
        if (!accounting || session.Status != PersistenceStatus.Ready || session.State.Paused)
        { estimator.Reset(); return; }
        if (Latest is null) return;
        // Finalization does not wait for the next polling tick.
        foreach (var observation in estimator.Finalize(clock.Now, Health))
        {
            try
            {
                var before = session.State;
                var result = session.Observe(observation);
                if (!before.BreakCompleted) maximumIncompleteBreakMs = Math.Max(maximumIncompleteBreakMs, before.BreakProgressMs);
                if (!result.State.BreakCompleted) maximumIncompleteBreakMs = Math.Max(maximumIncompleteBreakMs, result.State.BreakProgressMs);
                if (!before.BreakCompleted && !result.State.BreakCompleted && result.State.BreakProgressMs < before.BreakProgressMs)
                {
                    incompleteBreakDrops++;
                    if (result.Activity == ActivityKind.CountedPersonal) personalBreakDrops++;
                    else if (result.Activity == ActivityKind.Neutral) neutralBreakDrops++;
                    else if (result.Activity == ActivityKind.Uncertain)
                    {
                        uncertainBreakDrops++;
                        if (observation.Confidence == EvidenceConfidence.Unknown)
                        { if (observation.Continuity == ContinuityDisposition.Invalidate) invalidatingUnknownBreakDrops++; else transientUnknownBreakDrops++; }
                    }
                    else otherBreakDrops++;
                }
                EffectsCommitted += result.Effects.Length;
                var config = session.Configuration;
                var supportsPersonal = Health.Healthy && Latest.Desktop.Current == personal &&
                    Latest.Session == SessionState.Active && Latest.Input == Capability.Available &&
                    Latest.IdleAgeMs < config.IdleThresholdMs && Latest.Application == observation.Application &&
                    Latest.Foreground == Capability.Available && !result.State.Paused;
                if (!supportsPersonal) EffectsSuppressed += result.Effects.Count(x => x.Kind != EffectKind.AllowanceReset);
                if (!estimator.AcceptDelivery(observation, result)) break;
            }
            catch (CheckpointStorageException) { estimator.Reset(); break; }
        }
    }

    private void RefreshHealth()
    {
        if (source.Dropped != drops || source.CallbackErrors != errors)
        { generation++; drops = source.Dropped; errors = source.CallbackErrors; }
        Health = new(source.Hooks == Capability.Available && source.LoopHealth == Capability.Available &&
            source.SessionNotifications == Capability.Available && source.PowerNotifications == Capability.Available &&
            lastTick >= 0 && elapsed() - lastTick <= 2_000, generation);
    }

    private void Drain()
    {
        QueueHigh = Math.Max(QueueHigh, source.QueueDepth);
        while (source.TryTake(out var signal))
        {
            if (signal is null) continue;
            estimator.Signal(signal);
            if (signal.Kind == SignalKind.Foreground) foregroundSignals++;
            if (signal.Kind == SignalKind.Destroyed) destroyedSignals++;
            if (signal.Kind == SignalKind.InputDesktopChanged) desktopSignals++;
            if (signal.Kind == SignalKind.Session) sessionSignals++;
            if (signal.Kind == SignalKind.Power)
            { if (signal.Detail is 4 or 7 or 18) suspendResumeSignals++; else otherPowerSignals++; }
            if (signal.Kind is SignalKind.Foreground or SignalKind.InputDesktopChanged or SignalKind.Session or SignalKind.Power)
                eventSampleAt = Math.Min(eventSampleAt, elapsed() + 50);
            if (signal.Kind == SignalKind.Tick) lastTick = elapsed();
            if (signal.GeneratedTick is { } tick && TickMath.Age(tick, signal.ReceiptUptimeMs) is { } age)
                maximumDeliveryAgeMs = Math.Max(maximumDeliveryAgeMs, age);
            if (signal.Kind == SignalKind.Power && signal.Detail is 4 or 7 or 18)
            { newEpoch(); estimator.Reset(); generation++; }
        }
    }

    // Anonymous aggregate diagnostics only; no sample or application history.
    public object Diagnostics => new
    {
        Samples,
        MissedDeadlines,
        Recoveries,
        QueueHigh,
        EffectsCommitted,
        EffectsSuppressed,
        estimator.EstimatedMs,
        estimator.UnknownMs,
        estimator.InvalidatedIntervals,
        estimator.LateContradictions,
        accountingDelivery = new
        {
            estimator.CoreRejectedIntervals,
            estimator.CoreAcceptedEstimatedMs,
            estimator.CoreAcceptedUnknownMs,
            estimator.CoreLastReason
        },
        dropped = drops,
        callbackErrors = errors,
        commits = session.CommitDiagnostics,
        maximumDeliveryAgeMs,
        slowCycles,
        observationCycles = cycles.Snapshot(),
        schedulingDelay = scheduleDelay.Snapshot(),
        active = activeCycles.Snapshot(),
        idle = idleCycles.Snapshot(),
        locked = lockedCycles.Snapshot(),
        breakDiagnostics = new
        {
            maximumIncompleteBreakMs,
            incompleteBreakDrops,
            personalBreakDrops,
            neutralBreakDrops,
            uncertainBreakDrops,
            otherBreakDrops,
            invalidatingUnknownBreakDrops,
            transientUnknownBreakDrops
        },
        signalCounts = new { foregroundSignals, destroyedSignals, desktopSignals, sessionSignals, suspendResumeSignals, otherPowerSignals },
        foregroundDiagnostics = new { failures = foregroundFailures, lastFailure = lastForegroundFailure, lastForegroundIdentityQueryCode },
        providers = source.Diagnostics
    };
    public void Dispose() { estimator.Reset(); source.Dispose(); }
}
