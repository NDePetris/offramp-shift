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
    internal BreakReminderMailbox? Reminders { get; init; }
    private long reminderGeneration;
    private long reminderSignalSequence;
    private ApplicationIdentity? reminderApplication;
    private MonotonicTime? reminderEnd;
    private long offeredSequence;
    private readonly Guid deliveryEpoch = Guid.NewGuid();
    private ReminderEvent? offeredEvent;
    private long offeredHealthGeneration, authorizedSequence;
    private nint authorizedWindow;
    private string lastReminderInvalidation = "None";
    private NudgePresentation? presentation;
    private bool presentationCompleted;
    private string presentationFailure = "";
    private long presentationHealth;
    private uint presentationShell;
    private Guid presentationClock;
    private long presentationStartedAt;
    private nint presentationWindow, observedPresentationForeground;
    internal Guid PresentationId => presentationFailure.Length == 0 && presentation is { } p &&
        Environment.TickCount64 <= p.ExpiresTick && elapsed() - presentationStartedAt is >= 0 and <= 2_000 &&
        Health.Healthy && Health.Generation == presentationHealth && clock.Now.Epoch == presentationClock &&
        Latest?.Desktop.ShellProcess == presentationShell ? p.Id : Guid.Empty;
    internal long ReminderGeneration => reminderGeneration;
    internal Guid ReminderEpoch => deliveryEpoch;
    internal string ReminderStatus { get; private set; } = "No committed nudge event yet.";
    internal string NudgeAuthorizationFailure { get; private set; } = "Reminder eligibility changed or expired.";

    public bool CanKeepNudgeOpen => session.Status == PersistenceStatus.Ready && !session.State.Paused &&
        session.State.Exhausted && session.State.Intervention == InterventionState.Nudging &&
        HealthyPersonal();

    private bool HealthyPersonal() => Health.Healthy && Latest is { } p && p.Time.Epoch == clock.Now.Epoch &&
        clock.Now.Milliseconds - p.Time.Milliseconds is >= 0 and <= 2_000 &&
        p.Desktop.Health == Capability.Available && p.Desktop.Current == personal &&
        p.Desktop.Desktops.Contains(personal) && p.Desktop.Desktops.Contains(work) && p.Desktop.ShellProcess != 0 &&
        p.SessionHealth == Capability.Available && p.Session == SessionState.Active &&
        p.Input == Capability.Available && !p.InputDiscontinuity && p.IdleAgeMs is >= 0 && p.IdleAgeMs < configuration.IdleThresholdMs &&
        p.Foreground == Capability.Available && p.CollectionMs is >= 0 and <= 500;

    internal NudgePresentation? BeginNudgePresentation(ReminderEvent value)
    {
        NudgeAuthorizationFailure = "Reminder eligibility changed or expired.";
        presentation = null;
        Drain(); RefreshHealth();
        if (!ValidEvent(value)) return null;
        // Validate the ORIGINAL foreground before any WPF construction. A new
        // candidate has no shell identity yet and is verified strictly after Show.
        var membership = source.CheckNudgePresentation(value.ForegroundWindow, value.ForegroundWindow, personal, false);
        if (membership != NudgeDesktopCheck.Allowed)
        { NudgeAuthorizationFailure = "Desktop check: " + membership + "."; return null; }
        Drain(); RefreshHealth();
        if (!ValidEvent(value)) return null;
        // Consume the event exactly once. Layout now has a separate bounded contract;
        // its completion requires NEW safety evidence, not old accounting eligibility.
        authorizedSequence = value.Sequence;
        presentationHealth = Health.Generation;
        presentationShell = Latest!.Desktop.ShellProcess;
        presentationClock = clock.Now.Epoch;
        presentationStartedAt = elapsed();
        presentationWindow = observedPresentationForeground = 0;
        presentationCompleted = false; presentationFailure = "";
        return presentation = new(Guid.NewGuid(), value.Sequence, value.ForegroundWindow,
            personal, deliveryEpoch, Environment.TickCount64 + 2_000);
    }

    internal bool CompleteNudgePresentation(NudgePresentation value, nint window)
    {
        NudgeAuthorizationFailure = "PresentationUnknownOrConsumed";
        if (presentation != value || presentationCompleted || window == 0) return false;
        // No retry, even if a native query fails. Keep the transaction while draining
        // so external transitions during a query are still recorded and rejected.
        presentationCompleted = true;
        presentationWindow = window;
        var allowed = false;
        try
        {
            Drain(); RefreshHealth();
            if (!PresentationValid(value)) return false;
            if (observedPresentationForeground != 0 && observedPresentationForeground != window)
            { NudgeAuthorizationFailure = "ExternalForegroundSignal"; return false; }
            Latest = source.Read(); // Read-only safety capture; no estimator reset/credit.
            Drain(); RefreshHealth();
            if (!PresentationValid(value)) return false;
            if (!CanKeepNudgeOpen) { NudgeAuthorizationFailure = "FreshPersonalContextUnavailable"; return false; }
            if (Latest.Desktop.ShellProcess != presentationShell)
            { NudgeAuthorizationFailure = "ShellChanged"; return false; }
            if (Latest.Window != value.OriginalForeground && Latest.Window != window)
            { NudgeAuthorizationFailure = "ExternalForeground"; return false; }
            var membership = source.CheckNudgePresentation(window, Latest.Window, personal, false);
            if (membership != NudgeDesktopCheck.Allowed)
            { NudgeAuthorizationFailure = "Desktop check: " + membership + "."; return false; }
            Drain(); RefreshHealth();
            if (!PresentationValid(value)) return false;
            if (!CanKeepNudgeOpen) { NudgeAuthorizationFailure = "PersonalContextChangedAfterDesktopCheck"; return false; }
            authorizedWindow = window;
            allowed = true;
            return true;
        }
        finally { if (!allowed) presentation = null; }
    }

    private bool PresentationValid(NudgePresentation value)
    {
        var reason = presentationFailure.Length != 0 ? presentationFailure :
            Environment.TickCount64 > value.ExpiresTick || elapsed() - presentationStartedAt is < 0 or > 2_000 ? "PresentationExpired" :
            clock.Now.Epoch != presentationClock ? "ClockEpochChanged" :
            !Health.Healthy || Health.Generation != presentationHealth ? "PresentationHealthChanged" : "Allowed";
        if (reason == "Allowed") return true;
        NudgeAuthorizationFailure = reason;
        return false;
    }

    private bool ValidEvent(ReminderEvent value)
    {
        var reason = value != offeredEvent || value.Sequence != offeredSequence ? "EventReplaced" :
            value.Epoch != deliveryEpoch ? "RuntimeEpochChanged" :
            value.Sequence == authorizedSequence ? "AlreadyAuthorized" :
            offeredHealthGeneration != Health.Generation ? "HealthGenerationChanged" :
            value.Generation != reminderGeneration ? "ObservationGenerationChanged:" + lastReminderInvalidation :
            value.Style != ReminderStyle.InteractiveWindow || value.PersonalDesktop != personal ? "StyleOrDesktopChanged" :
            Environment.TickCount64 > value.ExpiresTick ? "EventExpired" :
            !CanKeepNudgeOpen ? "PersonalContextUnavailable" :
            !EligiblePersonal() ? "AccountingEvidenceNotReady:" + Reason :
            Latest!.Window != value.ForegroundWindow ? "ObservedForegroundChanged" : "Allowed";
        if (reason == "Allowed") return true;
        NudgeAuthorizationFailure = reason;
        return false;
    }

    internal bool CanDefer(nint window)
    {
        if (window == 0 || window != authorizedWindow) return false;
        Drain(); RefreshHealth();
        var before = reminderGeneration;
        // Commands precede Step on the owner thread. Capture now so queued context
        // changes cannot grant extra time against an earlier Personal snapshot.
        Latest = null;
        var point = CaptureCurrent();
        return before == reminderGeneration && HealthyPersonal() &&
            ApplicationClassifier.Classify(new(point.Time, point.Time, DesktopScope.Personal, point.Session,
                point.Application, InputState.Active, MonitoringAvailability.Available, EvidenceConfidence.Estimated), configuration) == ActivityKind.CountedPersonal;
    }

    private void InvalidateReminder() { Reminders?.Invalidate(); reminderApplication = null; reminderEnd = null; }

    private bool EligiblePersonal()
    {
        var now = clock.Now;
        return session.Status == PersistenceStatus.Ready && !session.State.Paused && Health.Healthy && Reason == "BoundedEstimate" &&
            Latest is { } p && p.Time.Epoch == now.Epoch && now.Milliseconds - p.Time.Milliseconds is >= 0 and <= 2_000 &&
            p.Desktop.Health == Capability.Available && p.Desktop.ShellProcess != 0 && p.Desktop.Current == personal &&
            p.Desktop.Desktops.Contains(personal) && p.Desktop.Desktops.Contains(work) &&
            p.SessionHealth == Capability.Available && p.Session == SessionState.Active &&
            p.Input == Capability.Available && !p.InputDiscontinuity && p.CollectionMs is >= 0 and <= 500 && p.IdleAgeMs is >= 0 &&
            p.IdleAgeMs < configuration.IdleThresholdMs && p.Foreground == Capability.Available &&
            p.Application is { Verified: true } && !string.IsNullOrWhiteSpace(p.Application.Key);
    }

    private void UpdateReminderEligibility(bool accounting, bool enabled)
    {
        if (Reminders is null) return;
        var now = clock.Now;
        var age = reminderEnd is { } end && end.Epoch == now.Epoch ? now.Milliseconds - end.Milliseconds : long.MaxValue;
        var maximumAge = Math.Min(EstimationPolicy.MaximumAgeMs, configuration.MaximumEvidenceAgeMs);
        var eligible = accounting && enabled && EligiblePersonal() && session.State.Exhausted &&
            session.State.Intervention == InterventionState.Nudging && Latest!.Application == reminderApplication &&
            age >= 0 && age <= maximumAge;
        var remaining = Latest is { } p ? 2_000 - (clock.Now.Milliseconds - p.Time.Milliseconds) : 0;
        remaining = Math.Min(remaining, maximumAge - age);
        // Health also has a two-second tick boundary; publication must not renew it.
        remaining = Math.Min(remaining, 2_000 - (elapsed() - lastTick));
        Reminders?.Update(eligible, Environment.TickCount64, remaining);
    }

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
        InvalidateReminder();
        // Retain the native stream sequence when providers have not changed.
        estimator.Reset();
        configuration = session.Configuration;
        estimator = new(personal, work, configuration);
        // Drain prior signals into the new estimator; sequence mismatch fails closed.
        Latest = null; Health = new(false, ++generation);
    }

    internal void RefreshInterventionConfiguration()
    {
        InvalidateReminder();
        configuration = session.Configuration;
        // Estimator uses accounting fields only; a cadence edit must preserve its continuity.
    }

    internal void RevokePresentation()
    { presentation = null; presentationWindow = observedPresentationForeground = 0; offeredSequence = 0; reminderGeneration++; InvalidateReminder(); }

    public bool RetryProviders()
    {
        InvalidateReminder();
        if (RetryInMs != 0) return false;
        lastRecovery = elapsed(); Recoveries++;
        // Failed cleanup must not create a second provider bundle.
        source.Dispose();
        source = factory();
        estimator.ResetSignals(); Latest = null; Health = new(false, ++generation);
        reminderSignalSequence = 0;
        lastTick = -1; drops = errors = 0;
        helperShellGeneration = shellGeneration = source.ShellGeneration;
        return true;
    }

    public EvidencePoint CaptureCurrent()
    {
        InvalidateReminder();
        estimator.Reset();
        Latest = source.Read();
        Drain(); RefreshHealth();
        return Latest;
    }

    public void Step(bool accounting = true, bool breakReminders = false, bool interactive = false)
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
            if (shellGeneration != 0 && shell != shellGeneration) { InvalidateReminder(); generation++; }
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
        UpdateReminderEligibility(accounting, breakReminders);
        if (!accounting || session.Status != PersistenceStatus.Ready || session.State.Paused)
        { estimator.Reset(); return; }
        if (Latest is null) return;
        // Finalization does not wait for the next polling tick.
        foreach (var observation in estimator.Finalize(clock.Now, Health))
        {
            try
            {
                InvalidateReminder();
                var deliveryGeneration = reminderGeneration;
                var healthGeneration = Health.Generation;
                var before = session.State;
                // Both enabled styles share immediate exhaustion and the same
                // Personal-only cadence. Off still accounts without delivering.
                var result = session.Observe(observation, true);
                if (result.Effects.Any(x => x.Kind == EffectKind.Nudge))
                    ReminderStatus = "Nudge committed; delivery eligibility checked next.";
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
                if (!breakReminders || Reminders is null) continue;
                // Saving can block; consume signals and recheck age/health after commit.
                Drain(); RefreshHealth();
                if (breakReminders && deliveryGeneration == reminderGeneration && healthGeneration == Health.Generation &&
                    clock.Now.Epoch == observation.End.Epoch &&
                    clock.Now.Milliseconds - observation.End.Milliseconds >= 0 &&
                    clock.Now.Milliseconds - observation.End.Milliseconds <= Math.Min(EstimationPolicy.MaximumAgeMs, configuration.MaximumEvidenceAgeMs) &&
                    result.Activity == ActivityKind.CountedPersonal && EligiblePersonal() &&
                    Latest!.Application == observation.Application && result.State.Exhausted &&
                    result.State.Intervention == InterventionState.Nudging)
                {
                    foreach (var effect in result.Effects.Where(x => x.Kind == EffectKind.Nudge))
                    {
                        reminderApplication = observation.Application;
                        reminderEnd = observation.End;
                        offeredSequence = effect.Sequence;
                        offeredHealthGeneration = Health.Generation;
                        var nowTick = Environment.TickCount64;
                        var remainingAge = Math.Min(EstimationPolicy.MaximumAgeMs, configuration.MaximumEvidenceAgeMs) -
                            (clock.Now.Milliseconds - observation.End.Milliseconds);
                        offeredEvent = new(effect.Sequence, reminderGeneration, Latest!.Window, personal,
                            nowTick + Math.Min(2_000, remainingAge), interactive ? ReminderStyle.InteractiveWindow : ReminderStyle.WindowsNotification, deliveryEpoch);
                        Reminders?.Offer(effect.Sequence, nowTick, offeredEvent);
                        ReminderStatus = "Committed nudge offered to the transient mailbox.";
                    }
                }
                UpdateReminderEligibility(accounting, breakReminders);
            }
            catch (CheckpointStorageException) { InvalidateReminder(); estimator.Reset(); break; }
        }
    }

    private void RefreshHealth()
    {
        if (source.Dropped != drops || source.CallbackErrors != errors)
        { InvalidateReminder(); generation++; drops = source.Dropped; errors = source.CallbackErrors; }
        Health = new(source.Hooks == Capability.Available && source.LoopHealth == Capability.Available &&
            source.SessionNotifications == Capability.Available && source.PowerNotifications == Capability.Available &&
            lastTick >= 0 && elapsed() - lastTick <= 2_000, generation);
    }

    private void Drain()
    {
        if (presentation is { } expired && (Environment.TickCount64 > expired.ExpiresTick || elapsed() - presentationStartedAt > 2_000))
        { presentation = null; presentationWindow = observedPresentationForeground = 0; }
        QueueHigh = Math.Max(QueueHigh, source.QueueDepth);
        while (source.TryTake(out var signal))
        {
            if (signal is null) continue;
            if (presentation is { } attempt)
            {
                // Accounting still sees EVERY signal below. Only this short-lived
                // presentation permits its own/original foreground transitions.
                if (signal.Sequence != reminderSignalSequence + 1) presentationFailure = "SignalSequenceDiscontinuity";
                else if (signal.Kind == SignalKind.Foreground && signal.Window != attempt.OriginalForeground)
                {
                    // One provisional HWND only, for at most two seconds. It is
                    // not trusted until bound to the exact owned UI candidate at
                    // completion and independently verified by the desktop worker.
                    if (signal.Window == 0 || presentationWindow != 0 && signal.Window != presentationWindow ||
                        observedPresentationForeground != 0 && signal.Window != observedPresentationForeground)
                        presentationFailure = "ExternalForegroundSignal";
                    else observedPresentationForeground = signal.Window;
                }
                else if (signal.Kind == SignalKind.Destroyed && (signal.Window == presentationWindow && presentationWindow != 0 || signal.Window == attempt.OriginalForeground))
                    presentationFailure = "PresentationWindowDestroyed";
                else if (signal.Kind is not (SignalKind.Tick or SignalKind.Foreground or SignalKind.Destroyed))
                    presentationFailure = "ContextSignal:" + signal.Kind;
            }
            if (signal.Kind != SignalKind.Tick || signal.Sequence != reminderSignalSequence + 1)
            { lastReminderInvalidation = signal.Kind.ToString(); reminderGeneration++; InvalidateReminder(); }
            reminderSignalSequence = Math.Max(reminderSignalSequence, signal.Sequence);
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
    public void Dispose() { RevokePresentation(); estimator.Reset(); source.Dispose(); }
}
