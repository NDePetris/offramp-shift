using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OfframpShift.ActivityMonitoring;
using OfframpShift.Core;
using OfframpShift.Persistence;
using ActivityKind = OfframpShift.Core.ActivityKind;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal static class MonitoringHost
{
    public static int Run(string[] args)
    {
        if (args.Length != 9 || !int.TryParse(args[1], out var seconds) || seconds is < 1 or > 86_400 ||
            args[2] != "--desktop" || args[3] != "--personal" || !Guid.TryParse(args[4], out var personal) ||
            args[5] != "--work" || !Guid.TryParse(args[6], out var work) || args[7] != "--store" ||
            personal == Guid.Empty || work == Guid.Empty || personal == work || string.IsNullOrWhiteSpace(args[8])) return 2;
        if (!DesktopWorker.IsSupported(DesktopWorker.Build, RuntimeInformation.ProcessArchitecture))
        { Commands.Write(new { result = "Unsupported", accounting = "Unavailable" }); return 2; }
        var clock = new ObservationClock();
        using var session = DurableMonitoringSession.Open(clock, new() { Accounting = AccountingMode.BestEffort }, args[8]);
        if (session.Status != PersistenceStatus.Ready)
        { Commands.Write(new { persistence = session.Status.ToString(), failure = session.Failure?.ToString() }); return 1; }
        if (session.Configuration.Accounting != AccountingMode.BestEffort)
            session.ChangeConfiguration(session.Configuration with { Accounting = AccountingMode.BestEffort });
        var configuration = session.Configuration;
        var estimator = new IntervalEstimator(personal, work, configuration);
        var loop = new NativeLoop(clock);
        DesktopWorker? worker = new();
        var sampler = new Sampler(clock, worker);
        var resources = new ResourceTotals();
        var cycles = new LatencyHistogram(); var scheduleDelay = new LatencyHistogram();
        var activeCycles = new LatencyHistogram(); var idleCycles = new LatencyHistogram(); var lockedCycles = new LatencyHistogram();
        var timer = Stopwatch.StartNew();
        var stop = false;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; Volatile.Write(ref stop, true); };
        Console.CancelKeyPress += cancel;
        long next = 0, lastTick = -1, generation = 0, drops = 0, errors = 0, queueHigh = 0;
        long missedDeadlines = 0, slowCycles = 0, effectsCommitted = 0, effectsSuppressed = 0, samples = 0, recoveries = 0;
        long nextResources = 0, lastRecovery = -10_000, maximumDeliveryAgeMs = 0;
        long eventSampleAt = long.MaxValue, lastSampleAt = -100;
        long maximumIncompleteBreakMs = 0, incompleteBreakDrops = 0;
        long personalBreakDrops = 0, neutralBreakDrops = 0, uncertainBreakDrops = 0, otherBreakDrops = 0;
        long invalidatingUnknownBreakDrops = 0, transientUnknownBreakDrops = 0;
        long foregroundSignals = 0, destroyedSignals = 0, desktopSignals = 0, sessionSignals = 0;
        long suspendResumeSignals = 0, otherPowerSignals = 0;
        var foregroundFailures = new Dictionary<ForegroundFailure, long>();
        ForegroundFailure lastForegroundFailure = ForegroundFailure.None;
        int? lastForegroundIdentityQueryCode = null;
        Native.GetWindowThreadProcessId(Native.GetShellWindow(), out var shellGeneration);
        var helperShellGeneration = shellGeneration;
        EvidencePoint? latest = null;
        ProviderHealth health = new(false, generation);
        Console.Error.WriteLine("Best-effort monitoring; final aggregate summary only. Ctrl+C stops. Pending commit retry: R. Explicit observation-provider retry: O (10 s cooldown). No notifications or switching.");
        try
        {
            while (timer.Elapsed.TotalSeconds < seconds && !Volatile.Read(ref stop))
            {
                Drain();
                if (!Console.IsInputRedirected && Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (key == ConsoleKey.R && session.Status == PersistenceStatus.PendingCommit)
                    {
                        try { session.RetryPendingCommit(); estimator.Reset(); generation++; }
                        catch (CheckpointStorageException) { }
                    }
                    if (key == ConsoleKey.O && timer.ElapsedMilliseconds - lastRecovery >= 10_000)
                    {
                        resources.Retire(worker?.OwnedProcess); worker?.Dispose(); loop.Dispose();
                        loop = new(clock); worker = new(); sampler = new(clock, worker);
                        estimator.ResetSignals(); generation++; lastTick = -1; drops = errors = 0;
                        lastRecovery = timer.ElapsedMilliseconds; recoveries++;
                        Native.GetWindowThreadProcessId(Native.GetShellWindow(), out helperShellGeneration);
                    }
                }
                if (timer.ElapsedMilliseconds >= next ||
                    timer.ElapsedMilliseconds >= eventSampleAt && timer.ElapsedMilliseconds - lastSampleAt >= 100)
                {
                    var periodic = timer.ElapsedMilliseconds >= next;
                    var lateness = periodic ? timer.ElapsedMilliseconds - next : 0;
                    scheduleDelay.Record(lateness * 1_000);
                    if (lateness >= 1_000) missedDeadlines += lateness / 1_000;
                    if (periodic) next = (timer.ElapsedMilliseconds / 1_000 + 1) * 1_000;
                    lastSampleAt = timer.ElapsedMilliseconds; eventSampleAt = long.MaxValue;
                    var started = Stopwatch.GetTimestamp();
                    Native.GetWindowThreadProcessId(Native.GetShellWindow(), out var shell);
                    if (shellGeneration != 0 && shell != shellGeneration)
                    {
                        generation++;
                    }
                    if (shell != 0 && shell != helperShellGeneration && timer.ElapsedMilliseconds - lastRecovery >= 10_000)
                    {
                        resources.Retire(worker?.OwnedProcess); worker?.Dispose(); worker = new();
                        sampler = new Sampler(clock, worker); lastRecovery = timer.ElapsedMilliseconds; recoveries++;
                        helperShellGeneration = shell;
                    }
                    shellGeneration = shell;
                    latest = sampler.Read(); samples++;
                    if (latest.Foreground == Capability.Unavailable)
                    {
                        foregroundFailures.TryGetValue(latest.ForegroundFailure, out var count);
                        foregroundFailures[latest.ForegroundFailure] = count + 1;
                        lastForegroundFailure = latest.ForegroundFailure;
                        lastForegroundIdentityQueryCode = latest.ForegroundIdentityQueryCode;
                    }
                    Drain();
                    if (loop.Dropped != drops || loop.CallbackErrors != errors)
                    { generation++; drops = loop.Dropped; errors = loop.CallbackErrors; }
                    health = new(loop.Hooks == Capability.Available && loop.LoopHealth == Capability.Available &&
                        loop.SessionNotifications == Capability.Available && loop.PowerNotifications == Capability.Available &&
                        lastTick >= 0 && timer.ElapsedMilliseconds - lastTick <= 2_000, generation);
                    if (session.Status == PersistenceStatus.Ready)
                    {
                        estimator.Point(latest, health);
                    }
                    else estimator.Reset();
                    var cycleUs = Stopwatch.GetElapsedTime(started).Ticks / 10;
                    cycles.Record(cycleUs);
                    if (cycleUs > 500_000) slowCycles++;
                    if (latest.Session == SessionState.Locked) lockedCycles.Record(cycleUs);
                    else if (latest.IdleAgeMs >= configuration.IdleThresholdMs) idleCycles.Record(cycleUs);
                    else activeCycles.Record(cycleUs);
                }
                Drain();
                if (loop.Dropped != drops || loop.CallbackErrors != errors)
                { generation++; drops = loop.Dropped; errors = loop.CallbackErrors; }
                health = new(loop.Hooks == Capability.Available && loop.LoopHealth == Capability.Available &&
                    loop.SessionNotifications == Capability.Available && loop.PowerNotifications == Capability.Available &&
                    lastTick >= 0 && timer.ElapsedMilliseconds - lastTick <= 2_000, generation);
                // Finalization has its own holdback deadline; it must not wait for a
                // second polling tick merely because snapshot latencies differ slightly.
                if (session.Status == PersistenceStatus.Ready && latest is not null)
                    foreach (var observation in estimator.Finalize(clock.Now, health))
                    {
                        try
                        {
                            var before = session.State;
                            var result = session.Observe(observation);
                            if (!before.BreakCompleted)
                                maximumIncompleteBreakMs = Math.Max(maximumIncompleteBreakMs, before.BreakProgressMs);
                            if (!result.State.BreakCompleted)
                                maximumIncompleteBreakMs = Math.Max(maximumIncompleteBreakMs, result.State.BreakProgressMs);
                            // Post-commit aggregate decreases only; no raw event or activity history.
                            if (!before.BreakCompleted && !result.State.BreakCompleted &&
                                result.State.BreakProgressMs < before.BreakProgressMs)
                            {
                                incompleteBreakDrops++;
                                if (result.Activity == ActivityKind.CountedPersonal) personalBreakDrops++;
                                else if (result.Activity == ActivityKind.Neutral) neutralBreakDrops++;
                                else if (result.Activity == ActivityKind.Uncertain)
                                {
                                    uncertainBreakDrops++;
                                    if (observation.Confidence == EvidenceConfidence.Unknown)
                                    {
                                        if (observation.Continuity == ContinuityDisposition.Invalidate) invalidatingUnknownBreakDrops++;
                                        else transientUnknownBreakDrops++;
                                    }
                                }
                                else otherBreakDrops++;
                            }
                            effectsCommitted += result.Effects.Length;
                            var supportsPersonal = health.Healthy && latest.Desktop.Current == personal &&
                                latest.Session == SessionState.Active && latest.Input == Capability.Available &&
                                latest.IdleAgeMs < configuration.IdleThresholdMs && latest.Application == observation.Application &&
                                latest.Foreground == Capability.Available && !result.State.Paused;
                            if (!supportsPersonal) effectsSuppressed += result.Effects.Count(x => x.Kind != EffectKind.AllowanceReset);
                            if (!estimator.AcceptDelivery(observation, result)) break;
                        }
                        catch (CheckpointStorageException)
                        { estimator.Reset(); Console.Error.WriteLine("Persistence blocked; accounting/effects stopped. Explicit pending retry: R."); break; }
                    }
                if (timer.ElapsedMilliseconds >= nextResources)
                { resources.Sample(worker?.OwnedProcess); nextResources = timer.ElapsedMilliseconds + 10_000; }
                Thread.Sleep(10);
            }
            resources.Sample(worker?.OwnedProcess);
            Commands.Write(new
            {
                accounting = "BestEffort",
                confidence = estimator.Reason == "BoundedEstimate" && health.Healthy ? "Estimated" : "Unknown",
                persistence = session.Status.ToString(),
                failure = session.Failure?.ToString(),
                state = session.Status == PersistenceStatus.Ready ? session.State : null,
                health = health.Healthy ? "Available" : "Unavailable",
                estimator.Reason,
                capabilities = new
                {
                    hooks = loop.Hooks.ToString(),
                    sessionNotifications = loop.SessionNotifications.ToString(),
                    powerNotifications = loop.PowerNotifications.ToString(),
                    foreground = latest?.Foreground.ToString(),
                    input = latest?.Input.ToString(),
                    desktop = latest?.Desktop.Health.ToString(),
                    bindings = latest is not null && latest.Desktop.Desktops.Contains(personal) && latest.Desktop.Desktops.Contains(work)
                        ? "Available" : "Unavailable"
                },
                build = DesktopWorker.Build,
                architecture = "x64",
                monitors = Native.GetSystemMetrics(80),
                elapsedMs = timer.ElapsedMilliseconds,
                samples,
                missedDeadlines,
                slowCycles,
                recoveries,
                queueHigh,
                queueDepth = loop.QueueDepth,
                dropped = loop.Dropped,
                callbackErrors = loop.CallbackErrors,
                maximumDeliveryAgeMs,
                estimator.EstimatedMs,
                estimator.UnknownMs,
                estimator.LateContradictions,
                estimator.InvalidatedIntervals,
                accountingDelivery = new
                {
                    estimator.CoreRejectedIntervals,
                    estimator.CoreAcceptedEstimatedMs,
                    estimator.CoreAcceptedUnknownMs,
                    estimator.CoreLastReason,
                    limitation = "Acknowledged Core results only; estimator totals can include rejected or discarded deliveries. No per-state history or certification."
                },
                effectsCommitted,
                effectsSuppressed,
                breakDiagnostics = new
                {
                    maximumIncompleteBreakMs,
                    incompleteBreakDrops,
                    personalBreakDrops,
                    neutralBreakDrops,
                    uncertainBreakDrops,
                    invalidatingUnknownBreakDrops,
                    transientUnknownBreakDrops,
                    otherBreakDrops,
                    limitation = "Observed post-commit decreases only; reset-and-credit within one transition may hide a decrease. Signal totals do not establish causal correlation."
                },
                signalCounts = new { foregroundSignals, destroyedSignals, desktopSignals, sessionSignals, suspendResumeSignals, otherPowerSignals },
                foregroundDiagnostics = new
                {
                    failures = foregroundFailures,
                    lastFailure = lastForegroundFailure.ToString(),
                    lastIdentityQueryCode = lastForegroundIdentityQueryCode,
                    limitation = "Anonymous failed-read categories only; no application identity, content or per-event history. Counts do not establish causal timing."
                },
                observationCycles = cycles.Snapshot(),
                schedulingDelay = scheduleDelay.Snapshot(),
                active = activeCycles.Snapshot(),
                idle = idleCycles.Snapshot(),
                locked = lockedCycles.Snapshot(),
                desktopIpc = sampler.DesktopLatency.Snapshot(),
                foregroundIdentity = sampler.ForegroundLatency.Snapshot(),
                sessionQueries = sampler.SessionLatency.Snapshot(),
                inputQueries = sampler.InputLatency.Snapshot(),
                commits = session.CommitDiagnostics,
                resources = resources.Snapshot(timer.Elapsed.TotalSeconds),
                limitation = "Bounded estimates; no historical certification or independent accounting acceptance. Final pending time is discarded."
            });
            return session.Status == PersistenceStatus.Ready && health.Healthy ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; try { worker?.Dispose(); } finally { loop.Dispose(); } }

        void Drain()
        {
            queueHigh = Math.Max(queueHigh, loop.QueueDepth);
            while (loop.TryTake(out var signal, 0))
            {
                if (signal is null) continue;
                estimator.Signal(signal);
                if (signal.Kind == SignalKind.Foreground) foregroundSignals++;
                if (signal.Kind == SignalKind.Destroyed) destroyedSignals++;
                if (signal.Kind == SignalKind.InputDesktopChanged) desktopSignals++;
                if (signal.Kind == SignalKind.Session) sessionSignals++;
                if (signal.Kind == SignalKind.Power)
                {
                    if (signal.Detail is 4 or 7 or 18) suspendResumeSignals++;
                    else otherPowerSignals++;
                }
                if (signal.Kind is SignalKind.Foreground or SignalKind.InputDesktopChanged or SignalKind.Session or SignalKind.Power)
                    eventSampleAt = Math.Min(eventSampleAt, timer.ElapsedMilliseconds + 50);
                if (signal.Kind == SignalKind.Tick) lastTick = timer.ElapsedMilliseconds;
                if (signal.GeneratedTick is { } tick && TickMath.Age(tick, signal.ReceiptUptimeMs) is { } age)
                    maximumDeliveryAgeMs = Math.Max(maximumDeliveryAgeMs, age);
                if (signal.Kind == SignalKind.Power && signal.Detail is 4 or 7 or 18)
                { clock.NewEpoch(); estimator.Reset(); generation++; }
            }
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class ResourceTotals
{
    private double retiredCpu;
    private long peakWorking, peakPrivate, initialPrivate, initialHandles, maximumHandles, maximumThreads;
    private double cpuSeconds;
    private ulong writeTransferBytes;
    public void Retire(Process? process)
    { if (process is not null && !process.HasExited) retiredCpu += process.TotalProcessorTime.TotalSeconds; }
    public void Sample(Process? helper)
    {
        using var host = Process.GetCurrentProcess();
        host.Refresh(); helper?.Refresh();
        var alive = helper is not null && !helper.HasExited;
        var working = host.WorkingSet64 + (alive ? helper!.WorkingSet64 : 0);
        var privateBytes = host.PrivateMemorySize64 + (alive ? helper!.PrivateMemorySize64 : 0);
        var handles = host.HandleCount + (alive ? helper!.HandleCount : 0);
        var threads = host.Threads.Count + (alive ? helper!.Threads.Count : 0);
        if (initialPrivate == 0) { initialPrivate = privateBytes; initialHandles = handles; }
        peakWorking = Math.Max(peakWorking, working); peakPrivate = Math.Max(peakPrivate, privateBytes);
        maximumHandles = Math.Max(maximumHandles, handles); maximumThreads = Math.Max(maximumThreads, threads);
        cpuSeconds = host.TotalProcessorTime.TotalSeconds + retiredCpu + (alive ? helper!.TotalProcessorTime.TotalSeconds : 0);
        if (GetProcessIoCounters(host.Handle, out var counters)) writeTransferBytes = counters.WriteTransfer;
    }
    public object Snapshot(double elapsed) => new
    {
        cpuPercentOfMachine = elapsed > 0 ? 100 * cpuSeconds / elapsed / Environment.ProcessorCount : 0,
        cpuSeconds,
        logicalProcessors = Environment.ProcessorCount,
        peakWorkingBytes = peakWorking,
        peakPrivateBytes = peakPrivate,
        initialPrivateBytes = initialPrivate,
        initialHandles,
        maximumHandles,
        maximumThreads,
        approximateHostWriteTransferBytes = writeTransferBytes,
        ioLimitation = "Process transfer bytes include non-disk IO; not physical SSD write volume."
    };
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadTransfer, WriteTransfer, OtherTransfer; }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(nint process, out IoCounters counters);
}
