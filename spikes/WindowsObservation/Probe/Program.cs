using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using OfframpShift.Core;
using WindowsObservation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        { Console.Error.WriteLine("Unsupported: Windows x64 required."); return 2; }
        try { return Commands.Run(args); }
        catch { Console.Error.WriteLine("ObservationProbeFailure; no implicit recovery or effect replay."); return 1; }
    }
}

[SupportedOSPlatform("windows")]
internal static class Commands
{
    public static int Run(string[] args)
    {
        if (args is ["--desktop-worker"]) return DesktopWorker.Run();
        if (args is ["--worker-hang"]) { Console.ReadLine(); Thread.Sleep(10_000); return 0; }
        if (args is ["--worker-malformed"]) { Console.ReadLine(); Console.WriteLine("{broken}"); return 0; }
        if (args is ["isolation-test"])
        {
            if (!DesktopWorker.IsSupported(DesktopWorker.Build, RuntimeInformation.ProcessArchitecture)) return 2;
            foreach (var mode in new[] { "--worker-hang", "--worker-malformed" })
            {
                using var worker = new DesktopWorker(mode);
                if (worker.Read(300).Health != Capability.Unavailable || worker.Read().Health != Capability.Unavailable) return 1;
                Console.WriteLine("PASS: owned worker failure reported, no retry or shell activation: " + mode);
            }
            return 0;
        }
        if (args is ["foreground-test"]) return ForegroundExercise.Run();
        if (args is ["user-test", "foreground"]) return UserObservationExercise.Run(false);
        if (args is ["user-test", "desktop"]) return UserObservationExercise.Run(true);
        if (args is ["delivery-test"]) return ForegroundExercise.DeliveryTest();
        if (args.Length > 0 && args[0] == "monitor") return MonitoringHost.Run(args);
        if (args.Length == 0 || args is ["help"])
        {
            Console.WriteLine("Windows observation investigation; live accounting disabled.\n" +
                "snapshot [--desktop]\nwatch <1..600 seconds> [--desktop] [--personal <GUID> --work <GUID>]\n" +
                "foreground-test (briefly shows two owned test windows; tests delayed hook delivery)\n" +
                "user-test foreground|desktop (operator-confirmed live endpoints; desktop is explicitly build-gated)\n" +
                "monitor <1..86400 seconds> --desktop --personal <GUID> --work <GUID> --store <directory>\n" +
                "delivery-test (hidden owned window; delays only our pump)\nisolation-test");
            return 0;
        }
        var watch = args[0] == "watch";
        if (!watch && args[0] != "snapshot") return 2;
        var seconds = 1;
        if (watch && (args.Length < 2 || !int.TryParse(args[1], out seconds) || seconds is < 1 or > 600)) return 2;
        var desktopEnabled = false;
        Guid? personal = null, work = null;
        for (var i = watch ? 2 : 1; i < args.Length; i++)
        {
            if (args[i] == "--desktop") desktopEnabled = true;
            else if (args[i] is "--personal" or "--work" && i + 1 < args.Length && Guid.TryParse(args[i + 1], out var id) && id != Guid.Empty)
            { if (args[i] == "--personal") personal = id; else work = id; i++; }
            else return 2;
        }
        if ((personal is null) != (work is null) || personal is not null && (personal == work || !desktopEnabled)) return 2;
        var clock = new ObservationClock();
        using var loop = new NativeLoop(clock);
        using var workerReader = desktopEnabled ? new DesktopWorker() : null;
        var sampler = new Sampler(clock, workerReader);
        var investigation = new EvidenceInvestigation();
        var timer = Stopwatch.StartNew();
        var stop = false;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; Volatile.Write(ref stop, true); };
        Console.CancelKeyPress += cancel;
        long foregroundEvents = 0, sessionEvents = 0, powerEvents = 0;
        long next = 0, lastTick = -1;
        long samples = 0, maximumLastInputAgeMs = 0, lastInputUpdates = 0;
        long? previousInputTimestamp = null;
        object? finalStatus = null;
        if (watch) Console.Error.WriteLine("Observing; only final current state and aggregate counters are printed. Ctrl+C stops.");
        try
        {
            do
            {
                if (loop.TryTake(out var signal, 100) && signal is not null)
                {
                    investigation.AcceptSignal(signal);
                    if (signal.Kind == SignalKind.Foreground) foregroundEvents++;
                    if (signal.Kind == SignalKind.Tick) lastTick = timer.ElapsedMilliseconds;
                    if (signal.Kind == SignalKind.Session) sessionEvents++;
                    if (signal.Kind == SignalKind.Power)
                    {
                        powerEvents++;
                        if (signal.Detail is 4 or 7 or 18) clock.NewEpoch();
                    }
                }
                if (timer.ElapsedMilliseconds >= next)
                {
                    var point = sampler.Read();
                    var result = investigation.AcceptPoint(point);
                    samples++;
                    if (point.IdleAgeMs is { } age)
                    {
                        maximumLastInputAgeMs = Math.Max(maximumLastInputAgeMs, age);
                        var timestamp = point.UptimeMs - age;
                        if (previousInputTimestamp is { } previous && timestamp > previous) lastInputUpdates++;
                        previousInputTimestamp = timestamp;
                    }
                    var scope = point.Desktop.Health == Capability.Available && personal is not null &&
                        point.Desktop.Desktops.Contains(personal.Value) && point.Desktop.Desktops.Contains(work!.Value)
                        ? point.Desktop.Current == personal ? DesktopScope.Personal : point.Desktop.Current == work ? DesktopScope.Work : DesktopScope.Other
                        : DesktopScope.Unknown;
                    finalStatus = new
                    {
                        build = DesktopWorker.Build,
                        architecture = "x64",
                        monitors = Native.GetSystemMetrics(80),
                        hooks = loop.Hooks.ToString(),
                        sessionNotifications = loop.SessionNotifications.ToString(),
                        powerNotifications = loop.PowerNotifications.ToString(),
                        pump = loop.LoopHealth != Capability.Available ? "Unavailable" : lastTick < 0 ? "AwaitingHeartbeat" :
                            timer.ElapsedMilliseconds - lastTick <= 2_000 ? "Responsive" : "Stalled",
                        foreground = point.Foreground.ToString(),
                        applicationKey = point.Application?.Key,
                        input = point.Input.ToString(),
                        lastInputAgeMs = point.IdleAgeMs,
                        session = point.Session.ToString(),
                        desktop = point.Desktop.Health.ToString(),
                        currentDesktop = point.Desktop.Current,
                        desktopCount = point.Desktop.Desktops.Length,
                        // Current diagnostic scope is never interval scope or verified continuity.
                        currentScope = scope.ToString(),
                        confidence = "Unknown",
                        accounting = "Gated",
                        certifiedMs = investigation.CertifiedMs,
                        uncertainMs = investigation.UncertainMs,
                        reason = result.Reason,
                        foregroundEvents,
                        sessionEvents,
                        powerEvents,
                        signals = investigation.Signals,
                        maximumDeliveryAgeMs = investigation.MaximumDeliveryAgeMs,
                        orderingFaults = investigation.OrderingFaults,
                        dropped = loop.Dropped,
                        callbackErrors = loop.CallbackErrors,
                        samples,
                        maximumLastInputAgeMs,
                        lastInputUpdates
                    };
                    next = timer.ElapsedMilliseconds + 1_000;
                }
            } while (watch && timer.Elapsed.TotalSeconds < seconds && !Volatile.Read(ref stop));
        }
        finally { Console.CancelKeyPress -= cancel; }
        if (finalStatus is not null) Write(finalStatus);
        // No Core coordinator or durable session is opened by the live harness.
        return loop.LoopHealth == Capability.Available && loop.CallbackErrors == 0 && loop.Dropped == 0 ? 0 : 1;
    }
    internal static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
}

[SupportedOSPlatform("windows")]
internal sealed class Sampler(ObservationClock clock, DesktopWorker? worker)
{
    private readonly LastInputTracker input = new();
    public OfframpShift.Persistence.LatencyHistogram DesktopLatency { get; } = new();
    public OfframpShift.Persistence.LatencyHistogram ForegroundLatency { get; } = new();
    public OfframpShift.Persistence.LatencyHistogram SessionLatency { get; } = new();
    public OfframpShift.Persistence.LatencyHistogram InputLatency { get; } = new();
    public EvidencePoint Read()
    {
        var started = Stopwatch.GetTimestamp();
        var batchStarted = started;
        var initialForeground = Native.GetForegroundWindow();
        var initialSession = Native.Session();
        SessionLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        started = Stopwatch.GetTimestamp();
        var desktop = worker?.Read() ?? new(Capability.Unavailable, null, [], 0);
        DesktopLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        started = Stopwatch.GetTimestamp();
        var foreground = Native.Foreground();
        ForegroundLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        started = Stopwatch.GetTimestamp();
        var session = Native.Session();
        SessionLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        started = Stopwatch.GetTimestamp();
        var value = new Native.LastInput { Size = (uint)Marshal.SizeOf<Native.LastInput>() };
        var succeeded = Native.GetLastInputInfo(ref value);
        var uptime = Environment.TickCount64;
        var idle = succeeded ? input.Read(value.Time, uptime) : null;
        ulong? awake = Native.QueryUnbiasedInterruptTime(out var awakeTime) ? awakeTime : null;
        InputLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        if (initialForeground != foreground.Window || Native.GetForegroundWindow() != foreground.Window)
        {
            if (foreground.Application is not null) foreground.Failure = ForegroundFailure.ForegroundChangedDuringCollection;
            foreground.Application = null;
        }
        if (initialSession != session) session = SessionState.Unknown;
        return new(clock.Now, uptime, awake, foreground.Window, foreground.Process, foreground.Application,
            idle, session, desktop, foreground.Application is not null ? Capability.Available : Capability.Unavailable,
            idle is not null ? Capability.Available : Capability.Unavailable,
            session != SessionState.Unknown ? Capability.Available : Capability.Unavailable)
        {
            CollectionMs = Stopwatch.GetElapsedTime(batchStarted).Ticks / TimeSpan.TicksPerMillisecond,
            InputDiscontinuity = succeeded && input.LastReadDiscontinuous,
            ForegroundFailure = foreground.Failure,
            ForegroundIdentityQueryCode = foreground.IdentityQueryCode
        };
    }
}
