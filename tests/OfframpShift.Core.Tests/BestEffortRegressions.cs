using OfframpShift.Core;

internal static class BestEffortRegressions
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        Fixture Make() => new(new() { Accounting = AccountingMode.BestEffort, RequiredBreakMs = 900_000 });
        TransitionResult Gap(Fixture f, long ms, bool soft = true) => f.Engine.Observe(f.Interval(ms,
            confidence: EvidenceConfidence.Unknown) with
        { Continuity = soft ? ContinuityDisposition.PreserveTransient : ContinuityDisposition.Invalidate });
        test("estimated charging is explicit and certified-only engines refuse it", () =>
        {
            var strict = new Fixture();
            check(strict.Send(1, confidence: EvidenceConfidence.Estimated).State.UsageMs == 0);
            var f = Make();
            check(f.Send(1_000, confidence: EvidenceConfidence.Estimated).State.EstimatedUsageMs == 1_000);
            check(f.Send(100).State.UsageMs == 1_100 && f.Engine.State.EstimatedUsageMs == 1_000);
            check(f.Send(1_501, confidence: EvidenceConfidence.Estimated).State.UsageMs == 1_100);
        });
        test("estimated idle does not require another sixty seconds", () =>
        {
            var f = Make();
            f.Send(1_000, confidence: EvidenceConfidence.Estimated);
            var idle = f.Send(1_000, input: InputState.QualifiedIdle, confidence: EvidenceConfidence.Estimated);
            check(idle.State.UsageMs == 1_000 && idle.State.BreakProgressMs == 1_000);
            check(f.Send(1_000, input: InputState.Idle, confidence: EvidenceConfidence.Estimated).Activity == ActivityKind.Uncertain);
            var strict = new Fixture();
            check(strict.Send(1, input: InputState.QualifiedIdle).Activity == ActivityKind.Uncertain);
        });
        test("estimated receipt age accepts two seconds exactly and rejects one extra millisecond", () =>
        {
            var valid = Make(); var interval = valid.Interval(1, confidence: EvidenceConfidence.Estimated);
            valid.Clock.Advance(2_000);
            check(valid.Engine.Observe(interval).State.UsageMs == 1);
            var stale = Make(); interval = stale.Interval(1, confidence: EvidenceConfidence.Estimated);
            stale.Clock.Advance(2_001);
            check(stale.Engine.Observe(interval).State.UsageMs == 0);
        });
        test("five second consecutive tolerance freezes fourteen minute progress", () =>
        {
            var f = Make(); f.Send(840_000, DesktopScope.Work);
            var gap = Gap(f, 5_000);
            check(gap.State.BreakProgressMs == 840_000 && gap.State.BreakUncertaintyMs == 5_000);
            check(gap.Effects.IsEmpty && gap.State.UsageMs == 0);
            check(Gap(f, 1).State.BreakProgressMs == 0);
            f.Send(840_000, DesktopScope.Work); Gap(f, 5_000);
            check(f.Send(60_000, DesktopScope.Work).State.BreakCompleted);
        });
        test("healthy credit never replenishes the fifteen second cumulative budget", () =>
        {
            var f = Make(); f.Send(1_000, DesktopScope.Work);
            for (var i = 0; i < 3; i++)
            {
                Gap(f, 5_000); f.Send(1_000, DesktopScope.Work);
                check(f.Engine.State.BreakUncertaintyMs == (i + 1) * 5_000);
            }
            check(Gap(f, 1).State.BreakProgressMs == 0);
        });
        test("hard uncertainty and Personal reset incomplete breaks without losing usage", () =>
        {
            var f = Make(); f.Send(1_000); f.Send(1_000, DesktopScope.Work);
            check(Gap(f, 1, false).State.BreakProgressMs == 0 && f.Engine.State.UsageMs == 1_000);
            f.Send(1_000, DesktopScope.Work); Gap(f, 1);
            check(f.Send(1).State.BreakUncertaintyMs == 0 && f.Engine.State.BreakProgressMs == 0);
        });
        test("unknown evidence cannot rearm a completed best-effort break", () =>
        {
            var f = Make(); f.Send(1_000); f.Send(900_000, DesktopScope.Work);
            var sequence = f.Engine.State.EffectSequence;
            Gap(f, 60_000, false);
            check(f.Send(1_000, DesktopScope.Work).Effects.IsEmpty && f.Engine.State.EffectSequence == sequence);
            check(f.Engine.State.BreakCompleted);
        });
        test("restoration retains estimated totals but abandons incomplete uncertainty", () =>
        {
            var f = Make(); f.Send(1_000, confidence: EvidenceConfidence.Estimated);
            f.Send(1_000, DesktopScope.Work); Gap(f, 100);
            var cp = f.Engine.CreateCheckpoint(); check(cp.Version == 3 && cp.IsValid(f.Clock.UtcNow));
            f.Restart(cp);
            check(f.Engine.State.EstimatedUsageMs == 1_000 && f.Engine.State.BreakUncertaintyMs == 0 && f.Engine.State.BreakProgressMs == 0);
        });
        test("selecting best-effort alone does not rearm a completed certified break", () =>
        {
            var f = new Fixture(); f.Send(1); f.Send(15, DesktopScope.Work);
            var sequence = f.Engine.State.EffectSequence;
            f.Engine.ChangeConfiguration(f.Engine.Configuration with { Accounting = AccountingMode.BestEffort });
            check(f.Engine.State.BreakCompleted);
            check(f.Send(1, DesktopScope.Work, confidence: EvidenceConfidence.Estimated).Effects.IsEmpty &&
                f.Engine.State.EffectSequence == sequence);
        });
    }
}
