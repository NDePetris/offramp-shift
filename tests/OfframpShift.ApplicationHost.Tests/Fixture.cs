using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;

internal sealed class FakeClock : IMonitoringClock
{
    public MonotonicTime Now { get; set; } = new(Guid.NewGuid(), 0);
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class FakeSource(Fixture fixture) : IObservationSource
{
    public Capability Hooks { get; set; } = Capability.Available;
    public Capability LoopHealth => Hooks;
    public Capability SessionNotifications => Hooks;
    public Capability PowerNotifications => Hooks;
    public long Dropped { get; set; }
    public long CallbackErrors { get; set; }
    public int QueueDepth => queue.Count;
    public uint ShellGeneration { get; set; } = 1;
    public bool Disposed { get; private set; }
    public int Replacements { get; private set; }
    public bool FailRead { get; set; }
    public int Reads { get; private set; }
    public Guid? Current { get; set; }
    public Guid[]? Topology { get; set; }
    public Capability Foreground { get; set; } = Capability.Available;
    public SessionState Session { get; set; } = SessionState.Active;
    public long IdleAgeMs { get; set; }
    public Capability Input { get; set; } = Capability.Available;
    public ApplicationIdentity Application { get; set; } = new("opaque-test", true);
    public bool NudgeDesktopValid { get; set; } = true;
    public Action? OnNudgeCheck { get; set; }
    public bool CheckNudgeDesktop(nint nudge, nint foreground, Guid personal)
    { OnNudgeCheck?.Invoke(); return NudgeDesktopValid && nudge != 0 && foreground == 1 && (Current ?? fixture.Personal) == personal; }
    private readonly Queue<NativeSignal> queue = new();
    private long sequence;
    public void Tick() => Emit(SignalKind.Tick);
    public void Emit(SignalKind kind, uint detail = 0) => queue.Enqueue(new(++sequence, kind, fixture.Clock.Now, fixture.Ms, null, Detail: detail));
    public EvidencePoint Read()
    {
        Reads++;
        if (FailRead) throw new InvalidOperationException("Injected provider failure");
        return new(fixture.Clock.Now, fixture.Ms, (ulong)fixture.Ms * 10_000, 1, 1, Application,
            IdleAgeMs, Session, new(Capability.Available, Current ?? fixture.Personal, Topology ?? [fixture.Personal, fixture.Work], ShellGeneration),
            Foreground, Input, Capability.Available);
    }
    public bool TryTake(out NativeSignal? signal) { var result = queue.TryDequeue(out var item); signal = item; return result; }
    public void ReplaceDesktopWorker() => Replacements++;
    public void Dispose() => Disposed = true;
}

internal sealed class Fixture : IDisposable
{
    public string PathName { get; } = Path.Combine(Path.GetTempPath(), "offramp-host-" + Guid.NewGuid().ToString("N"));
    public FakeClock Clock { get; } = new();
    public long Ms => Clock.Now.Milliseconds;
    public Guid Personal { get; } = Guid.NewGuid();
    public Guid Work { get; } = Guid.NewGuid();
    public FakeSource? Source { get; private set; }
    public int ProviderCreates { get; private set; }
    public Action<StorageFault>? Fault { get; set; }
    public bool FailCreate { get; set; }
    public WindowsSettingsStore Settings { get; }
    public Fixture() { Directory.CreateDirectory(PathName); Settings = new(PathName); Settings.Load(); }
    public DurableMonitoringSession Session() => new(Clock, new(), PathName, f => Fault?.Invoke(f));
    public IObservationSource CreateSource()
    {
        ProviderCreates++;
        if (FailCreate) throw new InvalidOperationException("Injected provider construction failure");
        Source = new(this); return Source;
    }
    public MonitoringApplication App(bool supported = true, string build = "26200.9457") => new(Clock, () => Clock.Now = new(Guid.NewGuid(), 0),
        () => Ms, CreateSource, Session, Settings, supported, build);
    public void Enable(bool monitoring = true, string build = "26200.9457")
    {
        Settings.Save(new() { Personal = Personal, Work = Work, Consent = true, MonitoringEnabled = monitoring, Build = build });
        using var session = Session();
        session.ChangeConfiguration(session.Configuration with { Accounting = AccountingMode.BestEffort });
    }
    public void Advance(MonitoringApplication app, long ms)
    { Clock.Now = Clock.Now with { Milliseconds = ms }; Source?.Tick(); app.Step(); }
    public void Dispose() { if (Directory.Exists(PathName)) Directory.Delete(PathName, true); }
}
