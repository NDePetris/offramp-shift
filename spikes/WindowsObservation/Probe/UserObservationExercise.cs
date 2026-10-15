using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OfframpShift.Core;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal static class UserObservationExercise
{
    public static int Run(bool desktop)
    {
        if (desktop && !DesktopWorker.IsSupported(DesktopWorker.Build, RuntimeInformation.ProcessArchitecture))
        {
            Commands.Write(new { result = "Unsupported", accounting = "Gated" });
            return 2;
        }
        var clock = new ObservationClock();
        using var loop = new NativeLoop(clock);
        using var worker = desktop ? new DesktopWorker() : null;
        var sampler = new Sampler(clock, worker);
        var gate = new UserObservationGate(desktop);
        var name = "OfframpUserObservation-" + Guid.NewGuid().ToString("N");
        var instance = Native.GetModuleHandle(null);
        nint a = 0, b = 0, requested = 0;
        var closed = false;
        // Only explicit test-button confirmations are queued; no input content is read.
        Native.WindowProcedure procedure = (hwnd, message, w, l) =>
        {
            if (message == 0x111 && (w & 0xffff) == 1 && (w >> 16) == 0)
            { if (requested == 0) requested = hwnd; return 0; }
            if (message == 0x10) { closed = true; return 0; }
            return Native.DefWindowProc(hwnd, message, w, l);
        };
        var value = new Native.WindowClass
        { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, Name = name };
        if (Native.RegisterClassEx(ref value) == 0) return 1;
        var timer = Stopwatch.StartNew();
        long lastA = 0, lastB = 0, acceptedSequence = 0, events = 0, maximumAge = 0;
        var clean = true;
        try
        {
            a = Window("Test A", 100);
            if (!desktop) b = Window("Test B", 550);
            if (a == 0 || !desktop && b == 0) gate.Stop("TestWindowCreationFailed");
            Console.Error.WriteLine(desktop
                ? "In Task View, pin Test A to all desktops yourself. Select Personal, then click Confirm. Switch manually as instructed; verify each role in Task View."
                : "Click Confirm in Test A, then Test B, alternating as instructed. No programmatic foreground activation is requested.");
            Prompt();
            while (!closed && gate.Failure is null && !gate.Complete && timer.Elapsed.TotalSeconds < 600)
            {
                while (Native.PeekMessage(out var message, 0, 0, 0, 1))
                { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                Drain();
                if (requested != 0)
                {
                    var expected = requested;
                    requested = 0;
                    var before = Native.GetForegroundWindow();
                    var started = timer.ElapsedMilliseconds;
                    var point = sampler.Read();
                    // Let asynchronous foreground hooks arrive without blocking the native pump.
                    // This is a local wait budget, never an OS completeness guarantee.
                    var deadline = timer.ElapsedMilliseconds + 500;
                    while (timer.ElapsedMilliseconds < deadline)
                    {
                        while (Native.PeekMessage(out var message, 0, 0, 0, 1))
                        { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                        Drain();
                        Thread.Sleep(10);
                    }
                    var after = Native.GetForegroundWindow();
                    var hook = expected == a ? lastA : lastB;
                    var healthy = loop.Hooks == Capability.Available && loop.LoopHealth == Capability.Available &&
                        loop.Dropped == 0 && loop.CallbackErrors == 0 && point.Session == SessionState.Active &&
                        timer.ElapsedMilliseconds - started <= 1_500 &&
                        (!desktop || point.Desktop.Health == Capability.Available);
                    gate.Confirm(expected == a, expected == before && expected == point.Window && expected == after,
                        point.Application is { Verified: true }, hook > acceptedSequence, healthy,
                        point.Desktop.Current, point.Desktop.Desktops);
                    acceptedSequence = Math.Max(lastA, lastB);
                    requested = 0; // A second click during the pending read cannot confirm another step.
                    if (closed) gate.Stop("OperatorStopped");
                    Prompt();
                }
                Thread.Sleep(10);
            }
            if (!gate.Complete && gate.Failure is null) gate.Stop(closed ? "OperatorStopped" : "OperatorTimeout");
        }
        finally
        {
            if (b != 0 && !Native.DestroyWindow(b)) clean = false;
            if (a != 0 && !Native.DestroyWindow(a)) clean = false;
            if (!Native.UnregisterClass(name, instance)) clean = false;
            GC.KeepAlive(procedure);
        }
        if (!clean) gate.Stop("TestWindowCleanupFailed");
        if (loop.Dropped != 0 || loop.CallbackErrors != 0 || loop.LoopHealth != Capability.Available)
            gate.Stop("ProviderOrDeliveryFailure");
        Commands.Write(new
        {
            exercise = desktop ? "OperatorDesktopEndpoints" : "OperatorForegroundTransitions",
            result = gate.Complete ? "Pass" : "Fail",
            reason = gate.Failure,
            confirmations = gate.Confirmations,
            transitions = Math.Max(0, gate.Confirmations - 1),
            build = DesktopWorker.Build,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            monitors = Native.GetSystemMetrics(80),
            elapsedMs = timer.ElapsedMilliseconds,
            foregroundEvents = events,
            maximumDeliveryAgeMs = maximumAge,
            dropped = loop.Dropped,
            callbackErrors = loop.CallbackErrors,
            accounting = "Gated",
            certifiedMs = 0,
            limitation = "Operator-confirmed endpoints only; no historical certification, accounting or application-classification acceptance"
        });
        return gate.Complete ? 0 : 1;

        nint Window(string label, int x)
        {
            var window = Native.CreateWindowEx(0, name, label, 0x10CF0000, x, 100, 420, 180, 0, 0, instance, 0);
            if (window != 0 && Native.CreateWindowEx(0, "BUTTON", "Confirm expected focus / desktop", 0x50010000,
                20, 40, 350, 40, window, 1, instance, 0) == 0)
            { Native.DestroyWindow(window); return 0; }
            return window;
        }
        void Prompt()
        {
            if (gate.Failure is not null || gate.Complete) return;
            var target = desktop ? gate.ExpectFirst ? "Personal" : "Work" : gate.ExpectFirst ? "Test A" : "Test B";
            var instruction = $"{gate.Confirmations + 1}/21: Confirm on {target}";
            Console.Error.WriteLine(instruction);
            if (a != 0) Native.SetWindowText(a, "Test A - " + instruction);
            if (b != 0) Native.SetWindowText(b, "Test B - " + instruction);
        }
        void Drain()
        {
            while (loop.TryTake(out var signal, 0))
            {
                if (signal is null || signal.Kind != SignalKind.Foreground) continue;
                events++;
                if (signal.Window == a) lastA = signal.Sequence;
                if (signal.Window == b) lastB = signal.Sequence;
                if (signal.GeneratedTick is { } tick && TickMath.Age(tick, signal.ReceiptUptimeMs) is { } age)
                    maximumAge = Math.Max(maximumAge, age);
            }
        }
    }
}
