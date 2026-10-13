using OfframpShift.Core;

internal sealed class FakeClock : IMonitoringClock
{
    public MonotonicTime Now { get; set; } = new(Guid.NewGuid(), 0);
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    public void Advance(long ms) => Now = Now with { Milliseconds = checked(Now.Milliseconds + ms) };
}

internal sealed class Fixture
{
    public FakeClock Clock { get; } = new();
    public MonitoringCoordinator Engine { get; set; }
    public static MonitoringConfiguration Small => new()
    {
        AllowanceMs = 30,
        RequiredBreakMs = 15,
        IdleThresholdMs = 6,
        GraceMs = 6,
        RepeatNudgeMs = 10,
        SnoozeMs = 10,
        MaximumEvidenceAgeMs = 5
    };

    public Fixture(MonitoringConfiguration? configuration = null) => Engine = new(Clock, configuration ?? Small);

    public MonitoringObservation Interval(long ms, DesktopScope desktop = DesktopScope.Personal,
        InputState input = InputState.Active, SessionState session = SessionState.Active,
        ApplicationIdentity? app = null, EvidenceConfidence confidence = EvidenceConfidence.Confirmed,
        MonitoringAvailability availability = MonitoringAvailability.Available)
    {
        var start = Clock.Now;
        Clock.Advance(ms);
        return new(start, Clock.Now, desktop, session, app ?? new("app", true), input, availability, confidence);
    }

    public TransitionResult Send(long ms, DesktopScope desktop = DesktopScope.Personal,
        InputState input = InputState.Active, SessionState session = SessionState.Active,
        ApplicationIdentity? app = null, EvidenceConfidence confidence = EvidenceConfidence.Confirmed,
        MonitoringAvailability availability = MonitoringAvailability.Available) =>
        Engine.Observe(Interval(ms, desktop, input, session, app, confidence, availability));

    public void Restart(MonitoringCheckpoint? checkpoint)
    {
        Clock.Now = new(Guid.NewGuid(), 0);
        Engine = MonitoringCoordinator.Restore(Clock, Small, checkpoint);
    }
}
