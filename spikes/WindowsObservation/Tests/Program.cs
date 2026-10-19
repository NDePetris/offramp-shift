using OfframpShift.Core;
using WindowsObservation;

var passed = 0;
var failed = 0;
var assertions = 0;
void Check(bool value)
{
    assertions++;
    if (!value) throw new InvalidOperationException("Assertion failed");
}
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS: " + name); }
    catch { failed++; Console.Error.WriteLine("FAIL: " + name); }
}
var epoch = Guid.NewGuid();
var knownDesktop = Guid.NewGuid();
EvidencePoint Point(long milliseconds, Guid? desktop = null, nint window = 1) => new(new(epoch, milliseconds), milliseconds,
    (ulong)Math.Max(0, milliseconds) * 10_000, window, 1, new("opaque", true), 0, SessionState.Active,
    new(Capability.Available, desktop ?? knownDesktop, [desktop ?? knownDesktop], 1), Capability.Available, Capability.Available, Capability.Available);
NativeSignal Signal(long sequence, long milliseconds, uint? generated = null) =>
    new(sequence, SignalKind.Foreground, new(epoch, milliseconds), milliseconds, generated);

Test("matching healthy endpoints never certify or charge", () =>
{
    var model = new EvidenceInvestigation();
    var clock = new FakeClock { Now = new(epoch, 0) };
    var engine = new MonitoringCoordinator(clock, new());
    Check(model.AcceptPoint(Point(0)).Interval is null);
    for (var i = 1; i <= 100; i++)
    {
        clock.Now = new(epoch, i * 1_000);
        var interval = model.AcceptPoint(Point(i * 1_000)).Interval!;
        Check(interval.Confidence == EvidenceConfidence.Unknown);
        var result = engine.Observe(interval);
        Check(result.Activity == ActivityKind.Uncertain && result.State.UsageMs == 0 && result.State.BreakProgressMs == 0 && result.Effects.Length == 0);
    }
    Check(model.CertifiedMs == 0 && model.UncertainMs == 100_000);
});
Test("change and return events do not prove intervening coverage", () =>
{
    var model = new EvidenceInvestigation();
    var a = Guid.NewGuid();
    model.AcceptPoint(Point(0, a));
    Check(model.AcceptSignal(Signal(1, 100, 100) with { Window = 2 }) == "SignalIsNotCoverage");
    Check(model.AcceptSignal(Signal(2, 200, 200) with { Window = 1 }) == "SignalIsNotCoverage");
    var result = model.AcceptPoint(Point(1_000, a));
    Check(result.Reason == "MatchingEndpointsAreNotCoverage" && result.Interval!.Confidence == EvidenceConfidence.Unknown);
});
Test("missing change and return remains uncertain despite matching snapshots", () =>
{
    var model = new EvidenceInvestigation();
    model.AcceptPoint(Point(0));
    Check(model.AcceptPoint(Point(1_000)).Interval!.Availability == MonitoringAvailability.Unavailable);
});
Test("changed foreground, desktop, session and shell are diagnostic contradictions", () =>
{
    foreach (var point in new[] { Point(1_000, window: 2), Point(1_000, Guid.NewGuid()),
        Point(1_000) with { Session = SessionState.Locked }, Point(1_000) with { Desktop = new(Capability.Unavailable, null, [], 2) } })
    {
        var model = new EvidenceInvestigation(); model.AcceptPoint(Point(0));
        var result = model.AcceptPoint(point);
        Check(result.Interval!.Confidence == EvidenceConfidence.Unknown && result.Reason != "MatchingEndpointsAreNotCoverage");
    }
});
Test("unavailable identity, privilege, input and desktop cannot earn break credit", () =>
{
    foreach (var point in new[] { Point(1_000) with { Application = null, Foreground = Capability.Unavailable },
        Point(1_000) with { Input = Capability.Unavailable, IdleAgeMs = null },
        Point(1_000) with { Session = SessionState.Unknown, SessionHealth = Capability.Unavailable },
        Point(1_000) with { Desktop = new(Capability.Unsupported, null, [], 0) },
        Point(1_000) with { Session = SessionState.Locked, IdleAgeMs = 99_000 } })
    {
        var model = new EvidenceInvestigation(); model.AcceptPoint(Point(0));
        Check(ApplicationClassifier.Classify(model.AcceptPoint(point).Interval!, new()) == ActivityKind.Uncertain);
    }
});
Test("duplicates, reordered signals and missing sequence are explicit", () =>
{
    var model = new EvidenceInvestigation();
    model.AcceptSignal(Signal(1, 100));
    Check(model.AcceptSignal(Signal(1, 100)) == "DuplicateOrReorderedSignal");
    Check(model.AcceptSignal(Signal(3, 200)) == "SignalGap");
    Check(model.AcceptSignal(Signal(2, 150)) == "DuplicateOrReorderedSignal");
    Check(model.OrderingFaults == 3);
});
Test("receipt rollback and native delay never repair accepted history", () =>
{
    var model = new EvidenceInvestigation();
    model.AcceptSignal(Signal(1, 500));
    Check(model.AcceptSignal(Signal(2, 499)) == "ReceiptRollback");
    Check(model.AcceptSignal(Signal(3, 3_000, 500)) == "DelayedNativeEvent");
    Check(model.MaximumDeliveryAgeMs == 2_500 && model.DelayedSignals == 1);
});
Test("long gap reports uncertainty without retrospective charging", () =>
{
    var model = new EvidenceInvestigation(); model.AcceptPoint(Point(0));
    var result = model.AcceptPoint(Point(30_000));
    Check(result.Reason == "ReconciliationGap" && result.Interval!.Start.Milliseconds == 0 && result.Interval.End.Milliseconds == 30_000);
    Check(model.AcceptPoint(Point(31_000)).Interval!.Start.Milliseconds == 30_000);
});
Test("same epoch rollback preserves high water and prevents overlap", () =>
{
    var model = new EvidenceInvestigation(); model.AcceptPoint(Point(100));
    Check(model.AcceptPoint(Point(200)).Interval is not null);
    Check(model.AcceptPoint(Point(150)).Interval is null);
    Check(model.AcceptPoint(Point(200)).Interval is null);
    Check(model.AcceptPoint(Point(250)).Interval!.Start.Milliseconds == 200);
});
Test("new epoch requires baseline and invalid clocks cannot move it", () =>
{
    var model = new EvidenceInvestigation(); model.AcceptPoint(Point(0));
    Check(model.AcceptPoint(Point(100) with { Time = new(Guid.Empty, 100) }).Interval is null);
    Check(model.AcceptPoint(Point(-1)).Interval is null);
    var nextEpoch = Guid.NewGuid();
    Check(model.AcceptPoint(Point(50) with { Time = new(nextEpoch, 0) }).Interval is null);
    var result = model.AcceptPoint(Point(60) with { Time = new(nextEpoch, 10) });
    Check(result.Interval!.Start == new MonotonicTime(nextEpoch, 0));
});
Test("sleep inclusive clock is checked against awake clock", () =>
{
    var model = new EvidenceInvestigation(); model.AcceptPoint(Point(0));
    Check(model.AcceptPoint(Point(1_000) with { Awake100ns = 10 }).Reason == "PossibleSuspendOrClockDiscontinuity");
    Check(model.AcceptPoint(Point(2_000) with { Awake100ns = 0 }).Reason == "PossibleSuspendOrClockDiscontinuity");
});
Test("input tick rollover, future and half wrap ambiguity", () =>
{
    Check(TickMath.Age(uint.MaxValue - 10, (long)uint.MaxValue + 10) == 20);
    Check(TickMath.Age(1_001, 1_000) is null);
    Check(TickMath.Age(0, -1) is null);
    Check(TickMath.Age(0, (long)int.MaxValue + 1) is null);
    Check(TickMath.Age(uint.MaxValue, 10) is null);
});
Test("last input regression discards reading and starts fresh", () =>
{
    var input = new LastInputTracker();
    Check(input.Read(100, 150) == 50);
    Check(input.Read(100, 200) == 100);
    Check(input.Read(99, 250) is null);
    Check(input.Read(260, 270) == 10);
});
Test("native read-only gate accepts approved exact builds and numeric 26300-family revisions on x64", () =>
{
    foreach (var build in new[] { "26200.9457", "26200.9711", "26300.0", "26300.9711", "26300.10000" })
    {
        Check(DesktopObservationPolicy.IsSupported(build, "X64"));
        foreach (var arch in new[] { "Arm64", "Arm", "X86", "x64", "", "unknown" })
            Check(!DesktopObservationPolicy.IsSupported(build, arch));
    }
    foreach (var build in new[] { "26200", "26200.9456", "26200.9458", "26200.9710", "26200.9712",
        "26100.9457", "26100.9711", "26201.9711", "26200.9711.0", " 26200.9711", "26200.9711 ",
        "26300", "263000.9711", "26300.-1", "26300.+1", "26300.9711.0", "26300.4294967296", "26400.9711", "", "unknown" })
        Check(!DesktopObservationPolicy.IsSupported(build, "X64"));
});
Test("generated evidence sequences preserve interval and accounting invariants", () =>
{
    var random = new Random(31013);
    var model = new EvidenceInvestigation();
    long highWater = 0, total = 0;
    model.AcceptPoint(Point(0));
    for (var i = 0; i < 5_000; i++)
    {
        var time = Math.Max(0, highWater + random.Next(-100, 3_000));
        var result = model.AcceptPoint(Point(time, window: random.Next(1, 5)));
        if (result.Interval is not { } interval) { Check(time <= highWater); continue; }
        Check(interval.Start.Milliseconds == highWater && interval.End.Milliseconds > highWater);
        Check(ApplicationClassifier.Classify(interval, new()) == ActivityKind.Uncertain);
        total += time - highWater; highWater = time;
    }
    Check(model.UncertainMs == total && model.CertifiedMs == 0);
});
Test("unknown live adapter output interrupts simulated partial break without resetting usage", () =>
{
    var clock = new FakeClock { Now = new(epoch, 0) };
    var engine = new MonitoringCoordinator(clock, new() { RequiredBreakMs = 10_000 });
    clock.Now = new(epoch, 1_000);
    engine.Observe(new(new(epoch, 0), clock.Now, DesktopScope.Personal, SessionState.Active, new("opaque", true),
        InputState.Active, MonitoringAvailability.Available, EvidenceConfidence.Confirmed));
    clock.Now = new(epoch, 6_000);
    engine.Observe(new(new(epoch, 1_000), clock.Now, DesktopScope.Work, SessionState.Active, null,
        InputState.Active, MonitoringAvailability.Available, EvidenceConfidence.Confirmed));
    Check(engine.State.BreakProgressMs == 5_000 && engine.State.UsageMs == 1_000);
    var model = new EvidenceInvestigation(); model.AcceptPoint(Point(6_000));
    clock.Now = new(epoch, 7_000);
    var result = engine.Observe(model.AcceptPoint(Point(7_000)).Interval!);
    Check(result.State.UsageMs == 1_000 && result.State.BreakProgressMs == 0 && result.Effects.Length == 0);
});

Test("worker shutdown timeout is reported rather than swallowed", () =>
{
    var kills = 0;
    try { WorkerShutdown.Stop(() => false, () => kills++, () => false); Check(false); }
    catch (TimeoutException) { Check(kills == 1); }
    WorkerShutdown.Stop(() => true, () => Check(false), () => { Check(false); return false; });
    Check(kills == 1);
});
Test("worker kill race is tolerated only after verified process exit", () =>
{
    var reads = 0;
    var waits = 0;
    WorkerShutdown.Stop(() => ++reads > 1, () => throw new InvalidOperationException(), () => { waits++; return true; });
    Check(reads == 2 && waits == 1);
    var rejected = false;
    try { WorkerShutdown.Stop(() => false, () => throw new InvalidOperationException(), () => { waits++; return true; }); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected && waits == 1);
});

Test("operator foreground gate requires twenty alternating observed transitions", () =>
{
    var gate = new UserObservationGate(false);
    for (var i = 0; i < UserObservationGate.RequiredConfirmations; i++)
        Check(gate.Confirm(i % 2 == 0, true, true, true, true, null, []));
    Check(gate.Complete && gate.Confirmations == 21);
    Check(!gate.Confirm(false, true, true, true, true, null, []));
});
Test("operator foreground gate refuses missing hook, identity, focus or health", () =>
{
    foreach (var missing in Enumerable.Range(0, 4))
    {
        var gate = new UserObservationGate(false);
        Check(!gate.Confirm(true, missing != 0, missing != 1, missing != 2, missing != 3, null, []));
        Check(!gate.Complete && gate.Failure is not null && gate.Confirmations == 0);
        Check(!gate.Confirm(true, true, true, true, true, null, []));
    }
    var repeated = new UserObservationGate(false);
    Check(repeated.Confirm(true, true, true, true, true, null, []));
    Check(!repeated.Confirm(true, true, true, true, true, null, []));
});
Test("operator desktop gate checks distinct stable GUIDs independent of foreground events", () =>
{
    var personal = Guid.NewGuid(); var work = Guid.NewGuid();
    var gate = new UserObservationGate(true);
    for (var i = 0; i < UserObservationGate.RequiredConfirmations; i++)
        Check(gate.Confirm(true, true, true, false, true, i % 2 == 0 ? personal : work, [work, personal]));
    Check(gate.Complete);
    var unchanged = new UserObservationGate(true);
    Check(unchanged.Confirm(true, true, true, false, true, personal, [personal, work]));
    Check(!unchanged.Confirm(true, true, true, false, true, personal, [personal, work]));
});
Test("operator desktop gate fails on missing bindings, invalid topology and wrong destination", () =>
{
    var personal = Guid.NewGuid(); var work = Guid.NewGuid(); var other = Guid.NewGuid();
    foreach (var topology in new Guid[][] { [], [personal], [personal, personal], [Guid.Empty, personal] })
    {
        var gate = new UserObservationGate(true);
        Check(!gate.Confirm(true, true, true, false, true, personal, topology));
    }
    foreach (var missing in new[] { true, false })
    {
        var gate = new UserObservationGate(true);
        Check(gate.Confirm(true, true, true, false, true, personal, [personal, work]));
        Check(gate.Confirm(true, true, true, false, true, work, [personal, work]));
        Check(!gate.Confirm(true, true, true, false, true, missing ? personal : other,
            missing ? [personal, other] : [personal, work, other]));
    }
});
Test("operator cancellation invalidates an otherwise completed gate", () =>
{
    var gate = new UserObservationGate(false);
    for (var i = 0; i < 21; i++) Check(gate.Confirm(i % 2 == 0, true, true, true, true, null, []));
    gate.Stop("CleanupFailed");
    Check(!gate.Complete);
});

EstimatorRegressions.Run(Test, Check);
DurableEstimatorRegression.Run(Test, Check);
Console.WriteLine($"{passed} tests, {assertions} assertions, {failed} failures; synthetic observation evidence and real local SQLite integration; no native observation APIs.");
return failed == 0 ? 0 : 1;

internal sealed class FakeClock : IMonitoringClock
{
    public MonotonicTime Now { get; set; }
    public DateTimeOffset UtcNow => new(2026, 10, 13, 14, 0, 0, TimeSpan.Zero);
}
