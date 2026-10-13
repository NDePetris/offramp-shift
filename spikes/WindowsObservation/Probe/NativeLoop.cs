using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OfframpShift.Core;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal sealed class NativeLoop : IDisposable
{
    private readonly ObservationClock clock;
    private readonly BlockingCollection<NativeSignal> queue = new(512);
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly ManualResetEventSlim stallStarted = new();
    private readonly Native.WinEvent callback;
    private readonly Native.WindowProcedure procedure;
    private nint window;
    private long sequence, dropped, callbackErrors;
    private bool disposed;
    private volatile bool stopRequested;
    public Capability Hooks { get; private set; } = Capability.Unavailable;
    public Capability SessionNotifications { get; private set; } = Capability.Unavailable;
    public Capability PowerNotifications { get; private set; } = Capability.Unavailable;
    public Capability LoopHealth { get; private set; } = Capability.Unavailable;
    public long Dropped => Interlocked.Read(ref dropped);
    public long CallbackErrors => Interlocked.Read(ref callbackErrors);

    public NativeLoop(ObservationClock clock)
    {
        this.clock = clock;
        callback = (_, evt, hwnd, obj, child, _, tick) =>
        {
            try
            {
                if (evt == 3) Emit(SignalKind.Foreground, tick, hwnd);
                else if (evt == 0x20) Emit(SignalKind.InputDesktopChanged, tick);
                else if (evt == 0x8001 && obj == 0 && child == 0) Emit(SignalKind.Destroyed, tick, hwnd);
            }
            catch { Interlocked.Increment(ref callbackErrors); }
        };
        procedure = WindowMessage;
        thread = new Thread(Run) { IsBackground = true, Name = "Observation message loop" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(3_000))
        {
            stopRequested = true;
            // An in-flight documented native call cannot be safely aborted. Let its owner clean up.
            thread.Join(5_000);
            throw new InvalidOperationException("MessageLoopStartupTimeout");
        }
    }
    private void Emit(SignalKind kind, uint? tick = null, nint hwnd = 0, uint detail = 0)
    {
        var signal = new NativeSignal(Interlocked.Increment(ref sequence), kind, clock.Now, Environment.TickCount64, tick, hwnd, detail);
        if (!queue.TryAdd(signal)) Interlocked.Increment(ref dropped);
    }
    private nint WindowMessage(nint hwnd, uint message, nuint w, nint l)
    {
        try
        {
            if (message == 0x113) { Emit(SignalKind.Tick); return 0; }
            if (message == 0x2B1) { Emit(SignalKind.Session, detail: (uint)w); return 0; }
            if (message == 0x218) { Emit(SignalKind.Power, detail: (uint)w); return 1; }
            if (message == 0x11 || message == 0x16) { Emit(SignalKind.Stopped); return message == 0x11 ? 1 : 0; }
            if (message == 0x8001)
            {
                // Explicit diagnostic fault exercise only. This intentionally delays OUR pump.
                Emit(SignalKind.PumpStall);
                stallStarted.Set();
                Thread.Sleep(w > 3_000 ? 3_000 : (int)w);
                Emit(SignalKind.PumpStall);
                return 0;
            }
            if (message == 0x10) { Native.PostQuitMessage(0); return 0; }
        }
        catch { Interlocked.Increment(ref callbackErrors); }
        return Native.DefWindowProc(hwnd, message, w, l);
    }
    private void Run()
    {
        var hooks = new List<nint>();
        nint power = 0;
        var name = "OfframpObservation-" + Guid.NewGuid().ToString("N");
        var instance = Native.GetModuleHandle(null);
        var registered = false;
        var session = false;
        var timerInstalled = false;
        try
        {
            var value = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, Name = name };
            registered = Native.RegisterClassEx(ref value) != 0;
            if (!registered) return;
            // Hidden top-level window; a message-only window does not receive general broadcasts.
            window = Native.CreateWindowEx(0, name, "Offramp observation diagnostics", 0, 0, 0, 0, 0, 0, 0, instance, 0);
            if (window == 0) return;
            foreach (var evt in new uint[] { 3, 0x20, 0x8001 })
            {
                var hook = Native.SetWinEventHook(evt, evt, 0, callback, 0, 0, 0);
                if (hook != 0) hooks.Add(hook);
            }
            Hooks = hooks.Count == 3 ? Capability.Available : Capability.Unavailable;
            session = Native.WTSRegisterSessionNotification(window, 0);
            SessionNotifications = session ? Capability.Available : Capability.Unavailable;
            power = Native.RegisterSuspendResumeNotification(window, 0);
            PowerNotifications = power != 0 ? Capability.Available : Capability.Unavailable;
            timerInstalled = Native.SetTimer(window, 1, 1_000, 0) != 0;
            LoopHealth = timerInstalled ? Capability.Available : Capability.Unavailable;
            ready.Set();
            if (stopRequested) return;
            int result;
            while ((result = Native.GetMessage(out var message, 0, 0, 0)) > 0)
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
            if (result < 0) Interlocked.Increment(ref callbackErrors);
        }
        catch { Interlocked.Increment(ref callbackErrors); }
        finally
        {
            ready.Set();
            foreach (var hook in hooks) if (!Native.UnhookWinEvent(hook)) Interlocked.Increment(ref callbackErrors);
            if (power != 0 && !Native.UnregisterSuspendResumeNotification(power)) Interlocked.Increment(ref callbackErrors);
            if (session && !Native.WTSUnRegisterSessionNotification(window)) Interlocked.Increment(ref callbackErrors);
            if (window != 0)
            {
                if (timerInstalled && !Native.KillTimer(window, 1)) Interlocked.Increment(ref callbackErrors);
                if (!Native.DestroyWindow(window)) Interlocked.Increment(ref callbackErrors);
                window = 0;
            }
            if (registered && !Native.UnregisterClass(name, instance)) Interlocked.Increment(ref callbackErrors);
            LoopHealth = Capability.Unavailable;
            Emit(SignalKind.Stopped);
            GC.KeepAlive(callback);
            GC.KeepAlive(procedure);
        }
    }
    public bool TryTake(out NativeSignal? signal, int milliseconds = 250) => queue.TryTake(out signal, milliseconds);
    public void StallForTest(int milliseconds)
    {
        if (milliseconds is < 1 or > 3_000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        stallStarted.Reset();
        if (!Native.PostMessage(window, 0x8001, (nuint)milliseconds, 0) || !stallStarted.Wait(1_000))
            throw new InvalidOperationException("PumpStallUnavailable");
    }
    public void Dispose()
    {
        if (disposed) return;
        if (thread.IsAlive && (!Native.PostMessage(window, 0x10, 0, 0) || !thread.Join(5_000)))
            throw new InvalidOperationException("MessageLoopShutdownFailed");
        disposed = true;
        queue.Dispose(); ready.Dispose(); stallStarted.Dispose();
        if (CallbackErrors != 0) throw new InvalidOperationException("NativeCallbackOrCleanupFailed");
    }
}
