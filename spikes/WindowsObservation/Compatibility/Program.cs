using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using WindowsObservation;

[assembly: SupportedOSPlatform("windows")]

namespace DesktopCompatibility;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["test"]) return SelfTests.Run();
            if (args is [var workerMode, var workerOptIn, var owner] && workerOptIn is Policy.OptIn or Policy.OptIn26300 &&
                workerMode.StartsWith("--worker-", StringComparison.Ordinal) &&
                int.TryParse(owner, out var parent)) return Worker(workerMode, parent, workerOptIn);
            if (!OperatingSystem.IsWindows()) return Reject("WindowsRequired");
            if (args is ["dry-run"])
            {
                var rejectedWithoutWorker = false;
                if (!ProductionSupported())
                {
                    using var guarded = new DesktopWorker();
                    rejectedWithoutWorker = guarded.OwnedProcess is null && guarded.Read().Health == Capability.Unsupported && DesktopWorker.Run() == 2;
                }
                Write(new
                {
                    stage = "EnvironmentOnlyNoCom",
                    environment = EnvironmentReport(),
                    eligibleForOptIn = Allowed(true),
                    productionSupported = ProductionSupported(),
                    productionEntryGuardsRejectedWithoutWorker = rejectedWithoutWorker,
                    comActivated = false
                });
                return 0;
            }
            Guid? expectedCurrent = null;
            if (args is ["once", var readOptIn, "--outside-tool-sandbox", "--expected-current", var expectedText])
            {
                if (!Guid.TryParse(expectedText, out var expected) || expected == Guid.Empty)
                    return Reject("InvalidExpectedCurrentDesktop");
                expectedCurrent = expected;
                args = ["once", readOptIn, "--outside-tool-sandbox"];
            }
            if (args is not [var mode, var optIn, "--outside-tool-sandbox"] ||
                !Policy.MatchesTarget(optIn, DesktopWorker.Build) || mode is not ("once" or "repeat" or "manual"))
                return Reject("ExplicitOptInAndOutsideSandboxAttestationRequired");
            if (!Allowed(true)) return Reject("EnvironmentRejected");
            return Exercise(mode, expectedCurrent);
        }
        catch { return Reject("DiagnosticInfrastructureFailure"); }
    }

    internal static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
    private static int Reject(string reason)
    {
        Write(new { result = "BLOCKED", reason, comActivationNotAuthorizedByThisResult = true });
        return 2;
    }

    private static string Release
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("DisplayVersion") as string ?? "";
        }
    }
    private static bool Elevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    private static bool Allowed(bool consent) => OperatingSystem.IsWindows() &&
        Policy.Allows(DesktopWorker.Build, RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.OSArchitecture.ToString(), Release, Elevated(), Environment.UserInteractive, consent);
    private static bool ProductionSupported() => DesktopObservationPolicy.IsSupported(
        DesktopWorker.Build, RuntimeInformation.ProcessArchitecture.ToString());
    private static object EnvironmentReport()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return new
        {
            build = DesktopWorker.Build,
            release = Release,
            edition = key?.GetValue("EditionID"),
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            elevated = Elevated(),
            interactive = Environment.UserInteractive,
            monitors = Native.GetSystemMetrics(80),
            runtime = RuntimeInformation.FrameworkDescription
        };
    }

    private static int Worker(string mode, int parentId, string optIn)
    {
        // A watchdog owns only this child. Parent death/closed pipes or absolute lifetime ends it.
        using var parent = Process.GetProcessById(parentId);
        var watchdog = new Thread(() =>
        {
            try { parent.WaitForExit(660_000); }
            catch { /* A lost parent handle also ends this disposable child. */ }
            Environment.Exit(3);
        })
        { IsBackground = true };
        watchdog.Start();
        if (mode != "--worker-live") return SelfTests.FaultWorker(mode);
        if (!Policy.MatchesTarget(optIn, DesktopWorker.Build) || !Allowed(true) ||
            Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return 2;
        try
        {
            using var reader = new DesktopComReader();
            for (var requests = 0; requests < 64 && Console.ReadLine() is { } request; requests++)
            {
                if (request != "read") return 2;
                var reading = reader.Read();
                if (!Policy.Valid(reading)) throw new DiagnosticFailure("InvalidReading");
                Write(new Reply(reading, null));
            }
            return 0;
        }
        catch (COMException) { Write(new Reply(null, "ComFailure")); return 1; }
        catch { Write(new Reply(null, "ReaderInvalidated")); return 1; }
    }

    private static int Exercise(string mode, Guid? expectedCurrent)
    {
        OwnedWorker? worker = null;
        string? failure = null;
        var cleanup = false;
        var windowCleanup = true;
        var cancelled = false;
        var latencies = new List<double>();
        var endpoints = new EndpointCheck();
        var gate = new UserObservationGate(true);
        var timer = Stopwatch.StartNew();
        var missedDeadlines = 0;
        var readErrors = 0;
        var timeouts = 0;
        var desktopCount = 0;
        Guid? baseline = null;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; Volatile.Write(ref cancelled, true); };
        Console.CancelKeyPress += cancel;
        try
        {
            worker = new OwnedWorker("--worker-live");
            if (mode == "manual") windowCleanup = Manual(Read, gate, () => Volatile.Read(ref cancelled));
            else
            {
                var count = mode == "once" ? 1 : 30;
                for (var i = 0; i < count; i++)
                {
                    if (Volatile.Read(ref cancelled)) throw new DiagnosticFailure("OperatorCancelled");
                    var started = Stopwatch.StartNew();
                    Read();
                    if (mode == "repeat" && started.ElapsedMilliseconds > 1_000) missedDeadlines++;
                    if (i + 1 < count) Thread.Sleep((int)Math.Max(0, 1_000 - started.ElapsedMilliseconds));
                }
            }
        }
        catch (DiagnosticFailure error)
        {
            failure = error.Category;
            if (failure == "TestWindowCleanupFailed") windowCleanup = false;
        }
        catch { failure = "DiagnosticInfrastructureFailure"; }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (worker is not null)
            {
                try { worker.Dispose(); cleanup = worker.CleanupConfirmed; }
                catch { failure = "WorkerCleanupFailed"; }
            }
        }
        if (!windowCleanup) failure = "TestWindowCleanupFailed";
        if (missedDeadlines != 0) failure ??= "SamplingDeadlineMissed";
        if (mode == "manual" && !gate.Complete) failure ??= gate.Failure ?? "IncompleteConfirmations";
        latencies.Sort();
        double? Percentile(double fraction) => latencies.Count == 0 ? null : latencies[(int)Math.Ceiling(latencies.Count * fraction) - 1];
        Write(new
        {
            exercise = mode,
            result = failure is null && cleanup ? "PASS" : "FAIL",
            failure,
            environment = EnvironmentReport(),
            productionSupported = ProductionSupported(),
            outsideToolSandbox = "OperatorAttested",
            readAttempts = latencies.Count,
            successfulReads = latencies.Count - readErrors,
            desktopCount,
            confirmations = gate.Confirmations,
            anonymousBaselineMatched = mode != "manual" ? failure is null : (bool?)null,
            expectedCurrentMatched = expectedCurrent.HasValue ? failure is null : (bool?)null,
            distinctBindingsExtantAndMatched = mode == "manual" ? gate.Complete : (bool?)null,
            providerHealth = readErrors == 0 && latencies.Count > 0 ? "Available" : "Unavailable",
            transitions = Math.Max(0, gate.Confirmations - 1),
            missedConfirmations = mode == "manual" ? 21 - gate.Confirmations : (int?)null,
            errors = failure is null ? 0 : 1,
            readErrors,
            timeouts,
            missedSamplingDeadlines = missedDeadlines,
            dropped = "NotApplicable_NoEventSubscriptionOrQueue",
            elapsedMs = timer.ElapsedMilliseconds,
            latencyMs = new { p50 = Percentile(.50), p95 = Percentile(.95), p99 = Percentile(.99), maximum = Percentile(1) },
            ownedWorkerExitConfirmed = cleanup,
            temporaryWindowCleanupConfirmed = windowCleanup,
            automaticRetries = 0,
            accounting = "Gated",
            persistentWrites = 0,
            limitation = "Read-only endpoint evidence; no event coverage, accounting accuracy or broad compatibility claim"
        });
        return failure is null && cleanup ? 0 : 1;

        DesktopReading Read()
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var reading = worker!.Read();
                endpoints.Accept(reading, expectedCurrent);
                if (mode != "manual")
                {
                    if (baseline is not null && baseline != reading.Current) throw new DiagnosticFailure("UnexpectedCurrentDesktop");
                    baseline ??= reading.Current;
                }
                desktopCount = reading.Desktops.Length;
                return reading;
            }
            catch (DiagnosticFailure error)
            {
                readErrors++;
                if (error.Category == "WorkerTimeout") timeouts++;
                throw;
            }
            catch { readErrors++; throw; }
            finally { latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
        }
    }

    private static bool Manual(Func<DesktopReading> read, UserObservationGate gate, Func<bool> cancelled)
    {
        var name = "OfframpCompatibility-" + Guid.NewGuid().ToString("N");
        var instance = Native.GetModuleHandle(null);
        var requested = false;
        var closed = false;
        var clean = true;
        nint window = 0;
        Native.WindowProcedure procedure = (hwnd, message, w, l) =>
        {
            if (message == 0x111 && (w & 0xffff) == 1 && (w >> 16) == 0) { requested = true; return 0; }
            if (message == 0x10) { closed = true; return 0; }
            return Native.DefWindowProc(hwnd, message, w, l);
        };
        var value = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, Name = name };
        if (Native.RegisterClassEx(ref value) == 0) throw new DiagnosticFailure("TestWindowCreationFailed");
        try
        {
            window = Native.CreateWindowEx(0, name, "Compatibility test", 0x10CF0000, 100, 100, 600, 200, 0, 0, instance, 0);
            if (window == 0 || Native.CreateWindowEx(0, "BUTTON", "I verified the expected desktop in Task View", 0x50010000,
                20, 40, 530, 40, window, 1, instance, 0) == 0) throw new DiagnosticFailure("TestWindowCreationFailed");
            Console.Error.WriteLine("Manually pin ONLY this temporary window to all desktops in Task View. Verify Personal/Work independently before EVERY confirmation. Closing cancels; ten-minute limit.");
            var timer = Stopwatch.StartNew();
            Prompt();
            while (!gate.Complete && gate.Failure is null && !closed && !cancelled() && timer.Elapsed.TotalSeconds < 600)
            {
                for (var i = 0; i < 100 && Native.PeekMessage(out var message, 0, 0, 0, 1); i++)
                { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                if (requested && !closed)
                {
                    requested = false;
                    if (Native.GetForegroundWindow() != window) throw new DiagnosticFailure("TestWindowNotForeground");
                    var reading = read();
                    if (Native.GetForegroundWindow() != window) throw new DiagnosticFailure("ForegroundChangedDuringRead");
                    gate.Confirm(true, true, true, false, true, reading.Current, reading.Desktops);
                    Prompt();
                }
                Thread.Sleep(10);
            }
            if (!gate.Complete) gate.Stop(gate.Failure ?? (closed || cancelled() ? "OperatorCancelled" : "OperatorTimeout"));
        }
        finally
        {
            if (window != 0 && !Native.DestroyWindow(window)) clean = false;
            if (!Native.UnregisterClass(name, instance)) clean = false;
            GC.KeepAlive(procedure);
            if (!clean) { gate.Stop("TestWindowCleanupFailed"); throw new DiagnosticFailure("TestWindowCleanupFailed"); }
        }
        return clean;

        void Prompt()
        {
            if (!gate.Complete && gate.Failure is null)
                Native.SetWindowText(window, $"{gate.Confirmations + 1}/21: Verify {(gate.ExpectFirst ? "Personal (A)" : "Work (B)")} in Task View, then confirm");
        }
    }
}
