using OfframpShift.Core;

internal static class ResetUsageRegressions
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        foreach (var scenario in new[] { "Grace", "Nudging", "Snoozed", "PartialBreak", "CompletedBreak", "Paused", "Estimated" })
            test("manual reset clears accounting and preserves configuration/Pause/sequence: " + scenario, () =>
            {
                var f = new Fixture(Fixture.Small with { Accounting = AccountingMode.BestEffort });
                f.Send(scenario == "Grace" ? 30 : 36);
                if (scenario == "Snoozed") f.Engine.Snooze();
                if (scenario == "PartialBreak") f.Send(5, DesktopScope.Work);
                if (scenario == "CompletedBreak") f.Send(15, DesktopScope.Work);
                if (scenario == "Paused") f.Engine.SetPaused(true);
                if (scenario == "Estimated") f.Send(1, confidence: EvidenceConfidence.Estimated);
                var before = f.Engine.State;
                var config = f.Engine.Configuration;
                f.Clock.Advance(100);
                var result = f.Engine.ResetUsage();
                check(result.CheckpointRequired && result.Effects.IsEmpty && result.Activity == ActivityKind.Uncertain);
                check(result.State == new EngineState(0, false, 0, false, 0, before.Paused,
                    InterventionState.Normal, 0, 0, before.EffectSequence, RecoveryStatus.Ready));
                check(f.Engine.Configuration == config && f.Engine.CreateCheckpoint().IsValid(f.Clock.UtcNow));
                var old = f.Interval(1) with { Start = new(f.Clock.Now.Epoch, 0) };
                check(f.Engine.Observe(old).Activity == ActivityKind.Uncertain && f.Engine.State.UsageMs == 0);
                if (before.Paused) f.Engine.SetPaused(false);
                var first = f.Send(1);
                check(first.State.UsageMs == 1 && first.State.EffectSequence == before.EffectSequence);
                var next = f.Send(35);
                check(next.Effects.All(x => x.Sequence > before.EffectSequence));
            });
        test("reset rejects regressing clock atomically and is unavailable in recovery", () =>
        {
            var f = new Fixture(); f.Send(36); var before = f.Engine.State;
            f.Clock.Now = f.Clock.Now with { Milliseconds = 0 };
            try { f.Engine.ResetUsage(); check(false); } catch (InvalidOperationException) { }
            check(f.Engine.State == before);
            f.Restart(null);
            try { f.Engine.ResetUsage(); check(false); } catch (InvalidOperationException) { }
            check(f.Engine.State.Recovery == RecoveryStatus.Required);
        });
    }
}
