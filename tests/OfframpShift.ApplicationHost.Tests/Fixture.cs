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
    public Action? OnRead { get; set; }
    public Guid? Current { get; set; }
    public Guid[]? Topology { get; set; }
    public Capability Foreground { get; set; } = Capability.Available;
    public SessionState Session { get; set; } = SessionState.Active;
    public long IdleAgeMs { get; set; }
    public Capability Input { get; set; } = Capability.Available;
    public ApplicationIdentity Application { get; set; } = new("opaque-test", true);
    public nint Window { get; set; } = 1;
    public bool UseRealUptime { get; set; }
    public bool NudgeDesktopValid { get; set; } = true;
    public NudgeDesktopCheck? NudgeCheckOverride { get; set; }
    public List<bool> NudgeCheckPhases { get; } = [];
    public Action? OnNudgeCheck { get; set; }
    public int CapturedNudgeChecks { get; private set; }
    public NudgeCheckResponse CaptureNudgePresentation(nint nudge, nint foreground, Guid personal, Guid work)
    {
        CapturedNudgeChecks++;
        var result = CheckNudgePresentation(nudge, foreground, personal, false);
        return new(result, new()
        {
            Result = result,
            Stage = result == NudgeDesktopCheck.Allowed ? NudgeCheckStage.Allowed : NudgeCheckStage.CandidateIdentity,
            CurrentBefore = DesktopIdentityCategory.Personal,
            CurrentMembershipCode = 0,
            OnCurrent = true,
            HostingIdentityCode = 0,
            HostingIdentity = result == NudgeDesktopCheck.Allowed ? DesktopIdentityCategory.Personal : DesktopIdentityCategory.Work
        });
    }
    public bool CheckNudgeDesktop(nint nudge, nint foreground, Guid personal)
    { OnNudgeCheck?.Invoke(); return NudgeDesktopValid && nudge != 0 && foreground == Window && (Current ?? fixture.Personal) == personal; }
    public NudgeDesktopCheck CheckNudgePresentation(nint nudge, nint foreground, Guid personal, bool prepareOnly)
    {
        NudgeCheckPhases.Add(prepareOnly);
        return NudgeCheckOverride ?? (CheckNudgeDesktop(nudge, foreground, personal) ? NudgeDesktopCheck.Allowed : NudgeDesktopCheck.QueryUnavailable);
    }
    private readonly Queue<NativeSignal> queue = new();
    private long sequence;
    public void Tick() => Emit(SignalKind.Tick);
    public void Emit(SignalKind kind, uint detail = 0, nint window = 0) => queue.Enqueue(new(++sequence, kind, fixture.Clock.Now, fixture.Ms, null, window, detail));
    public EvidencePoint Read()
    {
        Reads++;
        if (FailRead) throw new InvalidOperationException("Injected provider failure");
        OnRead?.Invoke();
        return new(fixture.Clock.Now, UseRealUptime ? Environment.TickCount64 : fixture.Ms, (ulong)fixture.Ms * 10_000, Window, 1, Application,
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
    public MonitoringApplication App(bool supported = true, string build = "26200.9457", Func<DesktopFeasibility.Configuration>? switchConfiguration = null) => new(Clock, () => Clock.Now = new(Guid.NewGuid(), 0),
        () => Ms, CreateSource, Session, Settings, supported, build, switchConfiguration);
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
