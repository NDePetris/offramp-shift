using System.Collections.Immutable;
using System.Text.Json;
using OfframpShift.Core;

var passed = 0;
var failed = 0;
var assertions = 0;
void Check(bool condition, string message = "Assertion failed")
{
    assertions++;
    if (!condition) throw new Exception(message);
}
void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}");
void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { assertions++; return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS: {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL: {name}: {ex.Message}"); }
}
int Effects(TransitionResult result, EffectKind kind) => result.Effects.Count(e => e.Kind == kind);
void Invariants(EngineState s, MonitoringConfiguration c)
{
    Check(s.UsageMs >= 0 && s.BreakProgressMs >= 0 && s.BreakProgressMs <= c.RequiredBreakMs);
    Check(s.IdleProgressMs >= 0 && s.IdleProgressMs <= c.IdleThresholdMs);
    Check(s.Exhausted || s.UsageMs < c.AllowanceMs);
    Equal(s.BreakCompleted, s.BreakProgressMs == c.RequiredBreakMs);
    Check(!s.BreakCompleted || s.UsageMs == 0);
    Check(s.Exhausted == (s.Intervention != InterventionState.Normal));
    Check(s.ReminderRemainingMs >= 0 && s.SnoozeRemainingMs >= 0);
    Check((s.Intervention == InterventionState.Snoozed) == (s.SnoozeRemainingMs > 0));
}

Test("configured defaults", () =>
{
    var c = new MonitoringConfiguration();
    c.Validate(); Equal(1_800_000L, c.AllowanceMs); Equal(900_000L, c.RequiredBreakMs);
    Equal(60_000L, c.IdleThresholdMs); Equal(60_000L, c.GraceMs);
    Equal(600_000L, c.RepeatNudgeMs); Equal(600_000L, c.SnoozeMs);
});
Test("both application modes and ordinal identity", () =>
{
    foreach (var mode in Enum.GetValues<ClassificationMode>())
    {
        var f = new Fixture(Fixture.Small with { Mode = mode, Applications = ["excluded"] });
        f.Send(2, app: new("excluded", true));
        Equal(mode == ClassificationMode.CountByDefault ? 0L : 2L, f.Engine.State.UsageMs);
        f.Send(3, app: new("other", true)); Equal(mode == ClassificationMode.CountByDefault ? 3L : 2L, f.Engine.State.UsageMs);
        f.Send(1, app: new("EXCLUDED", true)); Equal(mode == ClassificationMode.CountByDefault ? 4L : 2L, f.Engine.State.UsageMs);
    }
});
Test("missing, empty and unverifiable identity never count", () =>
{
    foreach (var mode in Enum.GetValues<ClassificationMode>())
        foreach (var app in new ApplicationIdentity?[] { null, new("", true), new("app", false) })
        {
            var f = new Fixture(Fixture.Small with { Mode = mode, Applications = ["app"] });
            var result = f.Engine.Observe(f.Interval(10) with { Application = app });
            Equal(ActivityKind.Uncertain, result.Activity); Equal(0L, result.State.UsageMs);
        }
});
Test("browsers are ordinary applications", () =>
{
    var f = new Fixture(); f.Send(4, app: new("chrome", true)); f.Send(5, app: new("msedge", true));
    Equal(9L, f.Engine.State.UsageMs);
});
Test("rules change prospectively without recalculation", () =>
{
    var f = new Fixture(); f.Send(12);
    f.Engine.ChangeConfiguration(Fixture.Small with { Applications = ["app"] });
    f.Send(5); Equal(12L, f.Engine.State.UsageMs);
    f.Engine.ChangeConfiguration(Fixture.Small with { Mode = ClassificationMode.BlocklistOnly, Applications = ["app"] });
    f.Send(3); Equal(15L, f.Engine.State.UsageMs);
});
Test("desktop transitions and short breaks preserve usage", () =>
{
    var f = new Fixture(); f.Send(20); f.Send(5, DesktopScope.Work); f.Send(2);
    Equal(22L, f.Engine.State.UsageMs); Equal(0L, f.Engine.State.BreakProgressMs);
    f.Send(9, DesktopScope.Other); Equal(22L, f.Engine.State.UsageMs); Equal(0L, f.Engine.State.BreakProgressMs);
});
Test("neutral and excluded activity interrupt break continuity", () =>
{
    foreach (var other in new[] { false, true })
    {
        var f = new Fixture(Fixture.Small with { Applications = ["excluded"] }); f.Send(10);
        f.Send(10, DesktopScope.Work); f.Send(1, other ? DesktopScope.Other : DesktopScope.Personal, app: new("excluded", true));
        f.Send(10, DesktopScope.Work); Equal(10L, f.Engine.State.UsageMs); Equal(10L, f.Engine.State.BreakProgressMs);
    }
});
Test("completed break resets once, then counted Personal rearms", () =>
{
    var f = new Fixture(); f.Send(29); var reset = f.Send(15, DesktopScope.Work);
    Equal(1, Effects(reset, EffectKind.AllowanceReset)); Equal(0L, reset.State.UsageMs);
    Equal(0, f.Send(50, DesktopScope.Work).Effects.Length);
    f.Send(2); Equal(1, Effects(f.Send(15, DesktopScope.Work), EffectKind.AllowanceReset));
});
Test("Personal inactivity beyond idle and break thresholds keeps counting without reset", () =>
{
    foreach (var threshold in new[] { 0L, 6L, 100L })
    {
        var f = new Fixture(Fixture.Small with { IdleThresholdMs = threshold }); f.Send(8);
        for (var i = 0; i < 200; i++)
        {
            var result = f.Send(1, input: InputState.Idle);
            Equal(0L, result.State.BreakProgressMs); Check(!result.State.BreakCompleted);
            Equal(0, Effects(result, EffectKind.AllowanceReset));
        }
        Equal(208L, f.Engine.State.UsageMs);
    }
});
Test("Work active and idle both reset once", () =>
{
    foreach (var input in new[] { InputState.Active, InputState.Idle, InputState.QualifiedIdle })
    {
        var f = new Fixture(); f.Send(3);
        Equal(1, Effects(f.Send(15, DesktopScope.Work, input), EffectKind.AllowanceReset));
        Equal(0, f.Send(30, DesktopScope.Work, input).Effects.Length);
    }
});
Test("idle Other and excluded Personal never earn usage or break", () =>
{
    foreach (var input in new[] { InputState.Active, InputState.Idle, InputState.QualifiedIdle })
        foreach (var scope in new[] { DesktopScope.Personal, DesktopScope.Other })
        {
            var f = new Fixture(Fixture.Small with { Applications = ["excluded"] });
            f.Send(4); f.Send(14, DesktopScope.Work);
            var result = f.Send(100, scope, input, app: new("excluded", true));
            Equal(ActivityKind.Neutral, result.Activity); Equal(4L, result.State.UsageMs);
            Equal(0L, result.State.BreakProgressMs); Equal(0, result.Effects.Length);
        }
});
Test("lock earns immediately, unlock continues only qualifying activity", () =>
{
    var f = new Fixture(); f.Send(10); f.Send(8, session: SessionState.Locked);
    f.Send(7, DesktopScope.Work); Equal(0L, f.Engine.State.UsageMs);
    f.Send(2); f.Send(8, session: SessionState.Locked); f.Send(1); f.Send(7, session: SessionState.Locked);
    Equal(3L, f.Engine.State.UsageMs); Equal(7L, f.Engine.State.BreakProgressMs);
});
Test("idle below threshold cannot bridge a Work break", () =>
{
    var f = new Fixture(); f.Send(4); f.Send(14, DesktopScope.Work); f.Send(6, input: InputState.Idle);
    Equal(0L, f.Engine.State.BreakProgressMs); f.Send(1, input: InputState.Idle); Equal(0L, f.Engine.State.BreakProgressMs); Equal(11L, f.Engine.State.UsageMs);
});
Test("pause freezes usage/timers and invalidates partial break", () =>
{
    var f = new Fixture(); f.Send(30); f.Send(2); f.Send(10, DesktopScope.Work);
    f.Engine.SetPaused(true); var remaining = f.Engine.State.ReminderRemainingMs;
    f.Send(100, DesktopScope.Work); f.Send(100); Equal(32L, f.Engine.State.UsageMs);
    Equal(remaining, f.Engine.State.ReminderRemainingMs); Equal(0L, f.Engine.State.BreakProgressMs);
    f.Engine.SetPaused(false); f.Send(5, DesktopScope.Work); Equal(32L, f.Engine.State.UsageMs);
    f.Send(10, DesktopScope.Work); Equal(0L, f.Engine.State.UsageMs);
});
Test("calendar rollover never resets", () =>
{
    var f = new Fixture(); f.Send(29); f.Clock.UtcNow = f.Clock.UtcNow.AddDays(10);
    f.Send(1); Check(f.Engine.State.Exhausted); Equal(30L, f.Engine.State.UsageMs);
});
Test("exact allowance and grace boundaries", () =>
{
    var f = new Fixture(); Equal(0, f.Send(29).Effects.Length);
    var exhausted = f.Send(1); Equal(1, Effects(exhausted, EffectKind.AllowanceExhausted));
    Equal(0, Effects(exhausted, EffectKind.Nudge)); Equal(InterventionState.Grace, exhausted.State.Intervention);
    Equal(0, f.Send(5).Effects.Length); Equal(1, Effects(f.Send(1), EffectKind.Nudge));
});
Test("exhaustion split within interval", () =>
{
    var f = new Fixture(); var result = f.Send(36);
    Equal(36L, result.State.UsageMs); Equal(1, Effects(result, EffectKind.AllowanceExhausted)); Equal(1, Effects(result, EffectKind.Nudge));
});
Test("zero grace nudges at exact exhaustion", () =>
{
    var f = new Fixture(Fixture.Small with { GraceMs = 0 }); Equal(1, Effects(f.Send(30), EffectKind.Nudge));
});
Test("grace and repeat use only counted Personal time", () =>
{
    var f = new Fixture(); f.Send(30); f.Send(5, DesktopScope.Work); f.Send(5, DesktopScope.Other);
    f.Send(5, input: InputState.Idle); Equal(1L, f.Engine.State.ReminderRemainingMs);
    f.Send(1); Equal(InterventionState.Nudging, f.Engine.State.Intervention);
    f.Send(5, DesktopScope.Work); f.Send(5, DesktopScope.Other); Equal(10L, f.Engine.State.ReminderRemainingMs);
    Equal(0, f.Send(9).Effects.Length); Equal(1, Effects(f.Send(1), EffectKind.Nudge));
});
Test("long interval coalesces missed reminders", () =>
{
    var f = new Fixture(); var result = f.Send(1000); Equal(1, Effects(result, EffectKind.Nudge));
    Equal(10L, result.State.ReminderRemainingMs); Equal(0, f.Send(9).Effects.Length);
});
Test("duplicate observation never duplicates effects or usage", () =>
{
    var f = new Fixture(); var interval = f.Interval(36); var first = f.Engine.Observe(interval);
    var repeat = f.Engine.Observe(interval); Equal(first.State, repeat.State); Equal(0, repeat.Effects.Length); Check(!repeat.CheckpointRequired);
});
Test("snooze counts verified Work/neutral time and leaves usage running", () =>
{
    var f = new Fixture(); f.Send(36); f.Engine.Snooze(); f.Send(3); Equal(39L, f.Engine.State.UsageMs);
    f.Send(3, DesktopScope.Work); f.Send(3, DesktopScope.Other); Equal(1L, f.Engine.State.SnoozeRemainingMs);
    Equal(1, Effects(f.Send(1), EffectKind.Nudge)); Equal(40L, f.Engine.State.UsageMs);
});
Test("snooze expiry on Work waits for counted Personal before nudge", () =>
{
    var f = new Fixture(); f.Send(36); f.Engine.Snooze(); Equal(0, f.Send(10, DesktopScope.Work).Effects.Length);
    Equal(InterventionState.Nudging, f.Engine.State.Intervention); Equal(1, Effects(f.Send(1), EffectKind.Nudge));
});
Test("snooze expiry inside Personal interval", () =>
{
    var f = new Fixture(); f.Send(36); f.Engine.Snooze(); Equal(1, Effects(f.Send(12), EffectKind.Nudge));
    Equal(48L, f.Engine.State.UsageMs); Equal(10L, f.Engine.State.ReminderRemainingMs);
});
Test("snooze does not advance while paused or uncertain", () =>
{
    var f = new Fixture(); f.Send(36); f.Engine.Snooze(); f.Engine.SetPaused(true); f.Send(100);
    Equal(10L, f.Engine.State.SnoozeRemainingMs); f.Engine.SetPaused(false);
    f.Send(100, confidence: EvidenceConfidence.Unknown); Equal(10L, f.Engine.State.SnoozeRemainingMs);
    f.Send(1); Equal(9L, f.Engine.State.SnoozeRemainingMs);
});
Test("snooze duplicate command is idempotent", () =>
{
    var f = new Fixture(); Equal(0, f.Engine.Snooze().Effects.Length); f.Send(36); f.Engine.Snooze(); f.Send(3);
    Check(!f.Engine.Snooze().CheckpointRequired); Equal(7L, f.Engine.State.SnoozeRemainingMs);
});
Test("snooze at a certified boundary preserves independent break", () =>
{
    var f = new Fixture(); f.Send(30); f.Send(10, DesktopScope.Work); f.Engine.Snooze();
    var result = f.Send(5, DesktopScope.Work); Equal(1, Effects(result, EffectKind.AllowanceReset));
    Equal(0L, result.State.UsageMs);
});
Test("break reset clears grace and snooze", () =>
{
    foreach (var snooze in new[] { false, true })
    {
        var f = new Fixture(); f.Send(30); if (snooze) f.Engine.Snooze();
        var result = f.Send(15, DesktopScope.Work); Equal(1, Effects(result, EffectKind.AllowanceReset));
        Equal(InterventionState.Normal, result.State.Intervention); Equal(0L, result.State.SnoozeRemainingMs);
        f.Send(1); Equal(1L, f.Engine.State.UsageMs);
    }
});
Test("allowance changes preserve totals and exhaustion latch", () =>
{
    var f = new Fixture(); f.Send(20); f.Engine.ChangeConfiguration(Fixture.Small with { AllowanceMs = 10 });
    Equal(20L, f.Engine.State.UsageMs); Check(f.Engine.State.Exhausted);
    f.Engine.ChangeConfiguration(Fixture.Small with { AllowanceMs = 100 }); Check(f.Engine.State.Exhausted);
    f.Send(1); Equal(21L, f.Engine.State.UsageMs); Check(f.Engine.CreateCheckpoint().IsValid(f.Clock.UtcNow));
});
Test("unknown/conflicting/unavailable evidence never earns or charges", () =>
{
    var f = new Fixture(); f.Send(5);
    var baseInterval = f.Interval(1);
    var bad = new[]
    {
        baseInterval with { Desktop = DesktopScope.Unknown }, baseInterval with { Session = SessionState.Unknown },
        baseInterval with { Input = InputState.Unknown }, baseInterval with { Confidence = EvidenceConfidence.Unknown },
        baseInterval with { Confidence = EvidenceConfidence.Conflicting },
        baseInterval with { Availability = MonitoringAvailability.Unavailable },
        baseInterval with { Availability = MonitoringAvailability.Unsupported },
        baseInterval with { Desktop = (DesktopScope)999 }
    };
    foreach (var template in bad)
    {
        f.Send(10, DesktopScope.Work);
        var interval = f.Interval(10) with
        {
            Desktop = template.Desktop,
            Session = template.Session,
            Input = template.Input,
            Confidence = template.Confidence,
            Availability = template.Availability
        };
        var result = f.Engine.Observe(interval); Equal(ActivityKind.Uncertain, result.Activity);
        Equal(5L, result.State.UsageMs); Equal(0L, result.State.BreakProgressMs); Equal(0, result.Effects.Length);
    }
});
Test("suspend/resume never infers sleep credit", () =>
{
    var f = new Fixture(); f.Send(5); f.Send(10, DesktopScope.Work);
    f.Send(1000, session: SessionState.Suspended); Equal(5L, f.Engine.State.UsageMs);
    f.Send(5, DesktopScope.Work); Equal(5L, f.Engine.State.BreakProgressMs);
});
Test("unexplained gap charges only certified interval and breaks streak", () =>
{
    var f = new Fixture(); f.Send(5); f.Send(10, DesktopScope.Work); f.Clock.Advance(1000);
    f.Send(5, DesktopScope.Work); Equal(5L, f.Engine.State.UsageMs); Equal(5L, f.Engine.State.BreakProgressMs);
    f.Clock.Advance(1000); f.Send(1); Equal(6L, f.Engine.State.UsageMs);
});
Test("delayed within freshness window is accepted; stale is rejected", () =>
{
    var f = new Fixture(); var interval = f.Interval(5); f.Clock.Advance(5); f.Engine.Observe(interval);
    Equal(5L, f.Engine.State.UsageMs);
    f = new Fixture(); interval = f.Interval(5); f.Clock.Advance(6); Equal(ActivityKind.Uncertain, f.Engine.Observe(interval).Activity);
    Equal(0L, f.Engine.State.UsageMs);
});
Test("overlap, conflict and out-of-order never double count", () =>
{
    var f = new Fixture(); var interval = f.Interval(10); f.Engine.Observe(interval);
    var overlapping = f.Interval(5) with { Start = interval.Start };
    Equal(0, f.Engine.Observe(overlapping).Effects.Length); Equal(10L, f.Engine.State.UsageMs);
    f.Engine.Observe(interval with { Desktop = DesktopScope.Work }); Equal(10L, f.Engine.State.UsageMs);
    f.Send(1); Equal(11L, f.Engine.State.UsageMs);
});
Test("invalid timestamps reject without moving boundary into future", () =>
{
    foreach (var variant in Enumerable.Range(0, 5))
    {
        var f = new Fixture(); var interval = f.Interval(5);
        interval = variant switch
        {
            0 => interval with { End = interval.Start },
            1 => interval with { Start = interval.Start with { Milliseconds = -1 } },
            2 => interval with { End = interval.End with { Milliseconds = 100 } },
            3 => interval with { Start = interval.Start with { Epoch = Guid.Empty } },
            _ => interval with { End = interval.End with { Epoch = Guid.NewGuid() } }
        };
        Equal(ActivityKind.Uncertain, f.Engine.Observe(interval).Activity); Equal(0L, f.Engine.State.UsageMs);
        f.Send(1); Equal(1L, f.Engine.State.UsageMs);
    }
});
Test("clock rollback cannot replay charged interval", () =>
{
    var f = new Fixture(); var interval = f.Interval(10); f.Engine.Observe(interval);
    f.Clock.Now = f.Clock.Now with { Milliseconds = 5 }; f.Engine.Observe(interval);
    f.Clock.Now = interval.End; f.Engine.Observe(interval); Equal(10L, f.Engine.State.UsageMs);
    f.Send(1); Equal(11L, f.Engine.State.UsageMs);
});
Test("new clock epoch requires baseline without discarding totals", () =>
{
    var f = new Fixture(); f.Send(10); f.Clock.Now = new(Guid.NewGuid(), 0);
    Equal(ActivityKind.Uncertain, f.Send(1).Activity); Equal(10L, f.Engine.State.UsageMs);
    f.Send(1); Equal(11L, f.Engine.State.UsageMs);
});
Test("commands discard unsupplied time", () =>
{
    var f = new Fixture(); var pending = f.Interval(10); f.Engine.ChangeConfiguration(Fixture.Small);
    f.Engine.Observe(pending); Equal(0L, f.Engine.State.UsageMs); f.Send(1); Equal(1L, f.Engine.State.UsageMs);
});
Test("command clock rollback rejects atomically", () =>
{
    var f = new Fixture(); f.Send(10); var before = f.Engine.State;
    f.Clock.Now = f.Clock.Now with { Milliseconds = 5 };
    Throws<InvalidOperationException>(() => f.Engine.SetPaused(true)); Equal(before, f.Engine.State);
    f.Clock.Now = f.Clock.Now with { Milliseconds = 10 }; f.Send(1); Equal(11L, f.Engine.State.UsageMs);
});
Test("valid checkpoint restores totals without downtime or partial break", () =>
{
    var f = new Fixture(); f.Send(20); f.Send(10, DesktopScope.Work); var checkpoint = f.Engine.CreateCheckpoint();
    f.Clock.UtcNow = f.Clock.UtcNow.AddYears(1); f.Restart(checkpoint);
    Equal(20L, f.Engine.State.UsageMs); Equal(0L, f.Engine.State.BreakProgressMs);
    f.Send(5, DesktopScope.Work); Equal(20L, f.Engine.State.UsageMs); f.Send(10, DesktopScope.Work); Equal(0L, f.Engine.State.UsageMs);
});
Test("recovery does not replay old observation", () =>
{
    var f = new Fixture(); var observation = f.Interval(20); f.Engine.Observe(observation); var checkpoint = f.Engine.CreateCheckpoint();
    f.Restart(checkpoint); Equal(0, f.Engine.Observe(observation).Effects.Length); Equal(20L, f.Engine.State.UsageMs);
    f.Send(1); Equal(21L, f.Engine.State.UsageMs);
});
Test("restored reminder/snooze state suppresses duplicate notifications", () =>
{
    var f = new Fixture(); f.Send(36); var checkpoint = f.Engine.CreateCheckpoint(); f.Restart(checkpoint);
    Equal(0, f.Send(9).Effects.Length); var next = f.Send(1); Equal(1, Effects(next, EffectKind.Nudge));
    Check(next.Effects[0].Sequence > checkpoint.State.EffectSequence);
    f.Engine.Snooze(); f.Send(3); checkpoint = f.Engine.CreateCheckpoint(); f.Restart(checkpoint);
    Equal(7L, f.Engine.State.SnoozeRemainingMs); Equal(0, f.Send(6).Effects.Length); Equal(1, Effects(f.Send(1), EffectKind.Nudge));
});
Test("restored grace retains only its confirmed Personal remainder", () =>
{
    var f = new Fixture(); f.Send(33); f.Restart(f.Engine.CreateCheckpoint());
    Equal(InterventionState.Grace, f.Engine.State.Intervention); Equal(3L, f.Engine.State.ReminderRemainingMs);
    f.Send(10, DesktopScope.Other); Equal(3L, f.Engine.State.ReminderRemainingMs);
    Equal(0, f.Send(2).Effects.Length); Equal(1, Effects(f.Send(1), EffectKind.Nudge));
});
Test("completed-break latch survives recovery", () =>
{
    var f = new Fixture(); f.Send(5); f.Send(15, DesktopScope.Work); var checkpoint = f.Engine.CreateCheckpoint(); f.Restart(checkpoint);
    Equal(0, f.Send(30, DesktopScope.Work).Effects.Length); f.Send(1); Equal(1, Effects(f.Send(15, DesktopScope.Work), EffectKind.AllowanceReset));
});
Test("Work and locked completed-break latches survive recovery", () =>
{
    foreach (var locked in new[] { false, true })
    {
        var f = new Fixture(); f.Send(5);
        f.Send(15, DesktopScope.Work, session: locked ? SessionState.Locked : SessionState.Active);
        f.Restart(f.Engine.CreateCheckpoint());
        var result = f.Send(30, DesktopScope.Work, session: locked ? SessionState.Locked : SessionState.Active);
        Equal(0, result.Effects.Length); Check(result.State.BreakCompleted);
        f.Send(1, input: InputState.Idle); Equal(1L, f.Engine.State.UsageMs);
    }
});
Test("paused checkpoint remains paused after restart", () =>
{
    var f = new Fixture(); f.Send(5); f.Engine.SetPaused(true); f.Restart(f.Engine.CreateCheckpoint());
    f.Send(100); Equal(5L, f.Engine.State.UsageMs); Check(f.Engine.State.Paused);
});
Test("checkpoint configuration restored before explicit overrides", () =>
{
    var f = new Fixture(Fixture.Small with { Applications = ["app"], AllowanceMs = 100 }); f.Send(5, app: new("other", true));
    f.Restart(f.Engine.CreateCheckpoint()); Equal(100L, f.Engine.Configuration.AllowanceMs);
    f.Send(5); Equal(5L, f.Engine.State.UsageMs);
    f.Engine.ChangeConfiguration(Fixture.Small); f.Send(5); Equal(10L, f.Engine.State.UsageMs);
});
Test("checkpoint round trip has no native/runtime identity", () =>
{
    var f = new Fixture(); f.Send(5); var json = JsonSerializer.Serialize(f.Engine.CreateCheckpoint());
    Check(!json.Contains("Epoch") && !json.Contains("Application\"") && !json.Contains("app\""));
    var checkpoint = JsonSerializer.Deserialize<MonitoringCheckpoint>(json); f.Restart(checkpoint);
    Equal(5L, f.Engine.State.UsageMs);
});
Test("unusable checkpoints require explicit tested reinitialization", () =>
{
    var f = new Fixture(); f.Send(20); var good = f.Engine.CreateCheckpoint();
    var bad = new MonitoringCheckpoint?[]
    {
        null, good with { Version = 999 }, good with { SavedUtc = default }, good with { SavedUtc = f.Clock.UtcNow.AddSeconds(1) },
        good with { Configuration = good.Configuration with { AllowanceMs = 0 } },
        good with { State = good.State with { UsageMs = -1 } },
        good with { State = good.State with { UsageMs = 30, Exhausted = false } },
        good with { State = good.State with { BreakProgressMs = 15, BreakCompleted = false } },
        good with { State = good.State with { SnoozeRemainingMs = 1 } },
        good with { State = good.State with { Intervention = (InterventionState)999 } },
        good with { State = good.State with { EffectSequence = -1 } },
        good with { State = good.State with { Recovery = RecoveryStatus.Required } },
        good with { State = good.State with { Paused = true, BreakProgressMs = 1 } },
        good with { Configuration = null! }, good with { State = null! }
    };
    foreach (var checkpoint in bad)
    {
        f.Restart(checkpoint); Equal(RecoveryStatus.Required, f.Engine.State.Recovery);
        Equal(0, f.Send(100).Effects.Length); Equal(RecoveryStatus.Required, f.Engine.State.Recovery);
        Throws<InvalidOperationException>(() => f.Engine.CreateCheckpoint());
        var result = f.Engine.Reinitialize(Fixture.Small); Check(result.CheckpointRequired);
        Equal(RecoveryStatus.Ready, result.State.Recovery); Equal(0L, result.State.UsageMs);
        f.Send(1); Equal(1L, f.Engine.State.UsageMs);
        Throws<InvalidOperationException>(() => f.Engine.Reinitialize(Fixture.Small));
    }
});
Test("invalid configuration rejected atomically", () =>
{
    var f = new Fixture(); f.Send(10); var before = f.Engine.State;
    var bad = new[]
    {
        Fixture.Small with { AllowanceMs = 0 }, Fixture.Small with { RequiredBreakMs = -1 },
        Fixture.Small with { IdleThresholdMs = -1 }, Fixture.Small with { GraceMs = -1 },
        Fixture.Small with { RepeatNudgeMs = 0 }, Fixture.Small with { SnoozeMs = 0 },
        Fixture.Small with { MaximumEvidenceAgeMs = 0 }, Fixture.Small with { Mode = (ClassificationMode)999 },
        Fixture.Small with { Applications = ["app", "app"] }, Fixture.Small with { Applications = [" "] },
        Fixture.Small with { Applications = default }
    };
    foreach (var c in bad)
    {
        Throws<ArgumentException>(c.Validate); Throws<ArgumentException>(() => f.Engine.ChangeConfiguration(c));
        Equal(before, f.Engine.State);
    }
});
Test("recovery commands cannot silently grant an allowance", () =>
{
    var f = new Fixture(); f.Restart(null); var before = f.Engine.State;
    f.Engine.SetPaused(false); f.Engine.Snooze(); f.Engine.ChangeConfiguration(Fixture.Small);
    Equal(before, f.Engine.State);
    Throws<ArgumentException>(() => f.Engine.Reinitialize(Fixture.Small with { AllowanceMs = 0 }));
    Equal(before, f.Engine.State);
});
Test("durable progress and uncertainty invalidation request checkpoints", () =>
{
    var f = new Fixture(); Check(f.Send(1).CheckpointRequired);
    Check(f.Send(1, DesktopScope.Work).CheckpointRequired);
    Check(f.Send(1, confidence: EvidenceConfidence.Conflicting).CheckpointRequired);
    Check(f.Engine.SetPaused(true).CheckpointRequired); Check(!f.Engine.SetPaused(true).CheckpointRequired);
    Check(!f.Send(100).CheckpointRequired); Check(f.Engine.SetPaused(false).CheckpointRequired);
});
Test("large durations saturate usage rather than overflowing", () =>
{
    var f = new Fixture(Fixture.Small with { AllowanceMs = long.MaxValue - 1, GraceMs = long.MaxValue, RepeatNudgeMs = long.MaxValue });
    f.Send(long.MaxValue - 2); f.Restart(f.Engine.CreateCheckpoint()); f.Send(10);
    Equal(long.MaxValue, f.Engine.State.UsageMs); Check(f.Engine.State.Exhausted); Invariants(f.Engine.State, f.Engine.Configuration);
});
Test("effect sequence overflow enters recovery without effects", () =>
{
    var f = new Fixture(); f.Send(36); var checkpoint = f.Engine.CreateCheckpoint();
    f.Restart(checkpoint with { State = checkpoint.State with { EffectSequence = long.MaxValue } });
    var result = f.Send(10); Equal(RecoveryStatus.Required, result.State.Recovery); Equal(0, result.Effects.Length);
    Equal(36L, result.State.UsageMs);
});
Test("transactional save precedes advisory delivery and restart", () =>
{
    var f = new Fixture(); var store = new MemoryStore(); var delivered = new List<AdvisoryEffect>();
    var result = f.Send(36); store.FailSave = true;
    Throws<IOException>(() => CommitAndDeliver()); Equal(0, delivered.Count);
    store.FailSave = false; CommitAndDeliver(); Equal(2, delivered.Count); f.Restart(store.Load());
    Equal(0, f.Send(1).Effects.Length);
    void CommitAndDeliver()
    {
        store.Save(f.Engine.CreateCheckpoint());
        delivered.AddRange(result.Effects);
    }
});
Test("crash after commit before delivery does not replay an advisory", () =>
{
    var f = new Fixture(); var store = new MemoryStore(); var result = f.Send(36);
    Equal(1, Effects(result, EffectKind.Nudge)); store.Save(f.Engine.CreateCheckpoint());
    // Deliberately never deliver result.Effects before restarting.
    f.Restart(store.Load()); Equal(0, f.Send(1).Effects.Length); Equal(37L, f.Engine.State.UsageMs);
});
Test("representative full 30-minute simulated session", () =>
{
    var f = new Fixture(new MonitoringConfiguration()); f.Send(20 * 60_000); f.Send(5 * 60_000, DesktopScope.Work);
    f.Send(2 * 60_000); Equal(22 * 60_000L, f.Engine.State.UsageMs);
    var result = f.Send(15 * 60_000, DesktopScope.Work); Equal(0L, result.State.UsageMs);
    Equal(1, Effects(result, EffectKind.AllowanceReset)); f.Send(60_000); Equal(60_000L, f.Engine.State.UsageMs);
});
Test("deterministic mixed sequences enforce invariants", () =>
{
    for (var seed = 0; seed < 20; seed++)
    {
        var random = new Random(seed); var f = new Fixture(); long lastEffect = 0;
        for (var i = 0; i < 200; i++)
        {
            var before = f.Engine.State; var action = random.Next(10); TransitionResult result;
            switch (action)
            {
                case 0: result = f.Engine.SetPaused(!before.Paused); break;
                case 1: result = f.Engine.Snooze(); break;
                case 2:
                    f.Restart(f.Engine.CreateCheckpoint()); result = f.Send(1); break;
                default:
                    var desktop = (DesktopScope)random.Next(4);
                    var input = random.Next(3) == 0 ? InputState.Idle : InputState.Active;
                    var confidence = random.Next(5) == 0 ? EvidenceConfidence.Unknown : EvidenceConfidence.Confirmed;
                    result = f.Send(random.Next(1, 20), desktop, input, confidence: confidence);
                    if (result.Activity == ActivityKind.Uncertain || before.Paused) Equal(before.UsageMs, result.State.UsageMs);
                    if (result.State.UsageMs < before.UsageMs) Equal(1, Effects(result, EffectKind.AllowanceReset));
                    if (before.Exhausted && !result.State.Exhausted) Equal(1, Effects(result, EffectKind.AllowanceReset));
                    if (before.BreakCompleted && result.Activity is ActivityKind.Work or ActivityKind.Locked)
                        Equal(0, Effects(result, EffectKind.AllowanceReset));
                    break;
            }
            Invariants(f.Engine.State, f.Engine.Configuration);
            Check(f.Engine.CreateCheckpoint().IsValid(f.Clock.UtcNow));
            foreach (var effect in result.Effects) { Check(effect.Sequence > lastEffect); lastEffect = effect.Sequence; }
        }
    }
});

ResetUsageRegressions.Run(Test, value => Check(value));
InteractiveRegressions.Run(Test, value => Check(value));
BestEffortRegressions.Run(Test, value => Check(value));
Console.WriteLine($"{passed} tests passed; {failed} failed; {assertions} assertions. Synthetic domain evidence only; no Windows integration validation.");
return failed == 0 ? 0 : 1;

internal sealed class MemoryStore : ICheckpointStore
{
    private MonitoringCheckpoint? checkpoint;
    public bool FailSave { get; set; }
    public MonitoringCheckpoint? Load() => checkpoint;
    public void Save(MonitoringCheckpoint replacement)
    {
        if (FailSave) throw new IOException("Simulated atomic save failure.");
        checkpoint = replacement;
    }
}
