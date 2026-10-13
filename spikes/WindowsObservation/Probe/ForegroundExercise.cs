using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal static class ForegroundExercise
{
    public static int DeliveryTest()
    {
        var clock = new ObservationClock();
        using var loop = new NativeLoop(clock);
        var name = "OfframpDeliveryTest-" + Guid.NewGuid().ToString("N");
        var instance = Native.GetModuleHandle(null);
        Native.WindowProcedure procedure = Native.DefWindowProc;
        var value = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, Name = name };
        if (Native.RegisterClassEx(ref value) == 0) return Fail("WindowClassUnavailable");
        nint window = 0;
        try
        {
            window = Native.CreateWindowEx(0, name, "Offramp delivery test", 0, 0, 0, 0, 0, 0, 0, instance, 0);
            if (window == 0) return Fail("OwnedWindowCreationFailed");
            var identity = window;
            Drain(loop);
            loop.StallForTest(2_500);
            if (!Native.DestroyWindow(window)) return Fail("OwnedWindowDestroyFailed");
            window = 0;
            Pump(2_700);
            var signals = Drain(loop);
            var age = signals.Where(s => s.Kind == SignalKind.Destroyed && s.Window == identity && s.GeneratedTick is not null)
                .Select(s => TickMath.Age(s.GeneratedTick!.Value, s.ReceiptUptimeMs) ?? 0).DefaultIfEmpty().Max();
            Commands.Write(new
            {
                stimulus = "HiddenOwnedWindowDestroy",
                observedDelayedEvent = age > 2_000,
                maximumDeliveryAgeMs = age,
                dropped = loop.Dropped,
                callbackErrors = loop.CallbackErrors,
                certifiedMs = 0,
                accounting = "Gated",
                guarantee = "ObservedDeliveryOnly"
            });
            return age > 2_000 && loop.Dropped == 0 && loop.CallbackErrors == 0 ? 0 : 1;
        }
        finally
        {
            if (window != 0) Native.DestroyWindow(window);
            Native.UnregisterClass(name, instance);
            GC.KeepAlive(procedure);
        }
    }

    // This drives only windows owned by the test process. It never switches desktops or sends input.
    public static int Run()
    {
        var clock = new ObservationClock();
        using var loop = new NativeLoop(clock);
        var initialForeground = Native.GetForegroundWindow();
        var name = "OfframpForegroundTest-" + Guid.NewGuid().ToString("N");
        var instance = Native.GetModuleHandle(null);
        Native.WindowProcedure procedure = Native.DefWindowProc;
        var value = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, Name = name };
        if (Native.RegisterClassEx(ref value) == 0) return Fail("WindowClassUnavailable");
        nint a = 0, b = 0;
        try
        {
            a = Native.CreateWindowEx(0, name, "Offramp observation test A", 0x10CF0000, 100, 100, 350, 150, 0, 0, instance, 0);
            b = Native.CreateWindowEx(0, name, "Offramp observation test B", 0x10CF0000, 140, 140, 350, 150, 0, 0, instance, 0);
            if (a == 0 || b == 0) return Fail("OwnedWindowCreationFailed");
            if (!Select(a)) return Fail("InitialForegroundStimulusRejected");
            Drain(loop);
            var before = Native.GetForegroundWindow();
            if (!Select(b) || !Select(a)) return Fail("ChangeAndReturnStimulusRejected");
            var after = Native.GetForegroundWindow();
            var normal = Drain(loop);
            var changeReturn = before == a && after == a && normal.Any(s => s.Kind == SignalKind.Foreground && s.Window == b) &&
                normal.Any(s => s.Kind == SignalKind.Foreground && s.Window == a);
            loop.StallForTest(2_500);
            if (!Select(b) || !Select(a)) return Fail("DelayedStimulusRejected");
            Pump(2_700);
            var delayed = Drain(loop);
            var deliveryAge = delayed.Where(s => s.Kind == SignalKind.Foreground && s.GeneratedTick is not null)
                .Select(s => TickMath.Age(s.GeneratedTick!.Value, s.ReceiptUptimeMs) ?? 0).DefaultIfEmpty().Max();
            var delayedReturn = delayed.Any(s => s.Kind == SignalKind.Foreground && s.Window == b) &&
                delayed.Any(s => s.Kind == SignalKind.Foreground && s.Window == a) && deliveryAge > 2_000;
            Commands.Write(new
            {
                stimulus = "OwnedWindowsOnly",
                matchingForegroundEndpoints = before == after,
                changeAndReturnObserved = changeReturn,
                delayedChangeAndReturnObserved = delayedReturn,
                maximumDeliveryAgeMs = deliveryAge,
                hooks = loop.Hooks.ToString(),
                dropped = loop.Dropped,
                callbackErrors = loop.CallbackErrors,
                certifiedMs = 0,
                accounting = "Gated",
                guarantee = "ObservedDeliveryOnly; no OS completeness or latency guarantee"
            });
            return changeReturn && delayedReturn && loop.Dropped == 0 && loop.CallbackErrors == 0 ? 0 : 1;
        }
        finally
        {
            if (b != 0) Native.DestroyWindow(b);
            if (a != 0) Native.DestroyWindow(a);
            Native.UnregisterClass(name, instance);
            // Best-effort focus restoration only; never an injected shortcut or a desktop operation.
            if (initialForeground != 0) Native.SetForegroundWindow(initialForeground);
            GC.KeepAlive(procedure);
        }
    }
    private static int Fail(string reason)
    {
        Commands.Write(new { stimulus = "OwnedWindowsOnly", result = "Unavailable", reason, certifiedMs = 0, accounting = "Gated" });
        return 1;
    }
    private static bool Select(nint window)
    {
        var requested = Native.SetForegroundWindow(window);
        Pump(150);
        return requested && Native.GetForegroundWindow() == window;
    }
    private static void Pump(int milliseconds)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            while (Native.PeekMessage(out var message, 0, 0, 0, 1))
            { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
            Thread.Sleep(10);
        }
    }
    private static List<NativeSignal> Drain(NativeLoop loop)
    {
        var result = new List<NativeSignal>();
        while (loop.TryTake(out var signal, 0)) if (signal is not null) result.Add(signal);
        return result;
    }
}
