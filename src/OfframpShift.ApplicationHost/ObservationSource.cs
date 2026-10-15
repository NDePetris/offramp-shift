using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WindowsObservation;

namespace OfframpShift.ApplicationHost;

public interface IObservationSource : IDisposable
{
    Capability Hooks { get; }
    Capability LoopHealth { get; }
    Capability SessionNotifications { get; }
    Capability PowerNotifications { get; }
    long Dropped { get; }
    long CallbackErrors { get; }
    int QueueDepth { get; }
    uint ShellGeneration { get; }
    EvidencePoint Read();
    bool TryTake(out NativeSignal? signal);
    void ReplaceDesktopWorker();
    object Diagnostics => new { };
}

[SupportedOSPlatform("windows")]
public sealed class WindowsObservationSource : IObservationSource
{
    private readonly ObservationClock clock;
    private readonly NativeLoop loop;
    private DesktopWorker? worker;
    private Sampler sampler;
    private readonly ResourceTotals resources = new();
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private long nextResourceSample;
    public static bool Supported => DesktopWorker.IsSupported(DesktopWorker.Build, RuntimeInformation.ProcessArchitecture);
    public static string Build => DesktopWorker.Build;
    public Capability Hooks => loop.Hooks;
    public Capability LoopHealth => loop.LoopHealth;
    public Capability SessionNotifications => loop.SessionNotifications;
    public Capability PowerNotifications => loop.PowerNotifications;
    public long Dropped => loop.Dropped;
    public long CallbackErrors => loop.CallbackErrors;
    public int QueueDepth => loop.QueueDepth;
    public uint ShellGeneration { get { Native.GetWindowThreadProcessId(Native.GetShellWindow(), out var shell); return shell; } }
    public WindowsObservationSource(ObservationClock clock)
    {
        if (!Supported) throw new InvalidOperationException("Unsupported Windows build or architecture.");
        this.clock = clock;
        loop = new(clock);
        try { worker = new(); sampler = new(clock, worker); }
        catch { loop.Dispose(); throw; }
    }
    public EvidencePoint Read()
    {
        var point = sampler.Read();
        if (lifetime.ElapsedMilliseconds >= nextResourceSample)
        { resources.Sample(worker?.OwnedProcess); nextResourceSample = lifetime.ElapsedMilliseconds + 10_000; }
        return point;
    }
    public object Diagnostics => new
    {
        desktopIpc = sampler.DesktopLatency.Snapshot(),
        foregroundIdentity = sampler.ForegroundLatency.Snapshot(),
        sessionQueries = sampler.SessionLatency.Snapshot(),
        inputQueries = sampler.InputLatency.Snapshot(),
        resources = resources.Snapshot(lifetime.Elapsed.TotalSeconds)
    };
    public bool TryTake(out NativeSignal? signal) => loop.TryTake(out signal, 0);
    public void ReplaceDesktopWorker()
    {
        resources.Retire(worker?.OwnedProcess); worker?.Dispose(); worker = null;
        worker = new(); sampler = new(clock, worker);
    }
    public void Dispose() { try { worker?.Dispose(); } finally { loop.Dispose(); } }
}
