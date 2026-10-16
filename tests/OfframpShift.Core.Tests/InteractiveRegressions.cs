using OfframpShift.Core;

internal static class InteractiveRegressions
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        Fixture Create() => new(Fixture.Small with { RequiredBreakMs = 600_000 });
        TransitionResult Personal(Fixture f, long ms) => f.Engine.Observe(f.Interval(ms), true);
        test("interactive exhaustion is immediate and repeat uses counted Personal only", () =>
        {
            var f = Create();
            var result = Personal(f, 30);
            check(result.Effects.Count(x => x.Kind == EffectKind.Nudge) == 1 && result.State.ReminderRemainingMs == 60_000);
            f.Send(10_000, DesktopScope.Work); f.Send(10_000, DesktopScope.Other);
            PersonalInterval(f, 1_000, input: InputState.Idle);
            PersonalInterval(f, 1_000, session: SessionState.Locked);
            PersonalInterval(f, 1_000, confidence: EvidenceConfidence.Unknown);
            f.Engine.SetPaused(true); Personal(f, 1_000); f.Engine.SetPaused(false);
            check(f.Engine.State.ReminderRemainingMs == 60_000);
            check(Personal(f, 59_999).Effects.Length == 0);
            check(Personal(f, 1).Effects.Count(x => x.Kind == EffectKind.Nudge) == 1);
            var legacy = Create(); check(legacy.Send(30).State.Intervention == InterventionState.Grace);
            check(legacy.Send(6).State.ReminderRemainingMs == 10);
        });
        TransitionResult PersonalInterval(Fixture f, long ms, InputState input = InputState.Active,
            SessionState session = SessionState.Active, EvidenceConfidence confidence = EvidenceConfidence.Confirmed) =>
            f.Engine.Observe(f.Interval(ms, input: input, session: session, confidence: confidence), true);
        foreach (var duration in new[] { 60_000L, 300_000L })
            test($"interactive {duration} extension freezes outside counted Personal and survives restart", () =>
            {
                var f = Create(); Personal(f, 30); f.Engine.DeferInteractive(duration);
                var initial = f.Engine.State;
                foreach (var scope in new[] { DesktopScope.Work, DesktopScope.Other }) f.Send(240_000, scope);
                f.Send(1_000, input: InputState.Idle); f.Send(1_000, session: SessionState.Locked);
                f.Send(1_000, confidence: EvidenceConfidence.Unknown);
                f.Engine.SetPaused(true); f.Send(1_000); f.Engine.SetPaused(false);
                check(f.Engine.State.SnoozeRemainingMs == duration && f.Engine.State.UsageMs == initial.UsageMs);
                check(!f.Engine.DeferInteractive(duration == 60_000 ? 300_000 : 60_000).CheckpointRequired);
                f.Restart(f.Engine.CreateCheckpoint());
                check(f.Engine.State.SnoozeRemainingMs == duration);
                check(Personal(f, duration - 1).Effects.Length == 0);
                check(Personal(f, 1).Effects.Count(x => x.Kind == EffectKind.Nudge) == 1);
                check(f.Engine.State.UsageMs == 30 + duration && f.Engine.State.Exhausted);
            });
        test("qualifying break clears interactive deferral and legacy snooze remains all-verified", () =>
        {
            var f = Create(); Personal(f, 30); f.Engine.DeferInteractive(300_000);
            var reset = f.Send(600_000, DesktopScope.Work);
            check(reset.State.UsageMs == 0 && reset.State.SnoozeRemainingMs == 0 && reset.State.Intervention == InterventionState.Normal);
            var legacy = Create(); legacy.Send(36); legacy.Engine.Snooze(); legacy.Send(5, DesktopScope.Work);
            check(legacy.Engine.State.SnoozeRemainingMs == 5);
        });
        test("interactive repeat edits preserve unfinished break usage pause and deferral", () =>
        {
            var f = Create(); Personal(f, 30); f.Engine.DeferInteractive(60_000); f.Send(10, DesktopScope.Work);
            var before = f.Engine.State; f.Engine.ChangeInteractiveRepeat(3_600_000);
            check(f.Engine.State == before && f.Engine.Configuration.InteractiveRepeatNudgeMs == 3_600_000);
            foreach (var bad in new[] { 0L, 59_999, 60_001, 3_600_001, long.MaxValue })
            {
                try { f.Engine.ChangeInteractiveRepeat(bad); check(false); } catch (ArgumentException) { }
                check(f.Engine.State == before);
            }
            foreach (var bad in new[] { 0L, 1, 120_000, long.MaxValue })
            { try { f.Engine.DeferInteractive(bad); check(false); } catch (ArgumentException) { } }
        });
        test("duration edits preserve shared cadence and cadence reduction clamps prospectively", () =>
        {
            var f = Create(); f.Engine.ChangeInteractiveRepeat(3_600_000); Personal(f, 30);
            f.Engine.ChangeConfiguration(f.Engine.Configuration with { AllowanceMs = 100 });
            check(f.Engine.State.ReminderRemainingMs == 3_600_000 && f.Engine.State.UsageMs == 30);
            f.Engine.ChangeInteractiveRepeat(120_000);
            check(f.Engine.State.ReminderRemainingMs == 120_000);
            check(Personal(f, 119_999).Effects.Length == 0);
            check(Personal(f, 1).Effects.Count(x => x.Kind == EffectKind.Nudge) == 1);
        });
        test("changing from legacy grace to interactive waits for fresh counted evidence", () =>
        {
            var f = Create(); f.Send(30); f.Engine.Observe(f.Interval(1_000, DesktopScope.Work), true);
            check(f.Engine.State.Intervention == InterventionState.Grace);
            check(Personal(f, 1).Effects.Count(x => x.Kind == EffectKind.Nudge) == 1);
        });
    }
}
