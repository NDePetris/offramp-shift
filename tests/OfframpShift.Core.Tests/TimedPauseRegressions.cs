using OfframpShift.Core;

internal static class TimedPauseRegressions
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("timed pause uses UTC boundary once and emits no catch-up effects", () =>
        {
            var f = new Fixture(); f.Send(36); f.Engine.Snooze();
            var deadline = f.Clock.UtcNow.AddHours(1);
            f.Engine.SetPaused(true, deadline); var paused = f.Engine.State;
            f.Send(100_000, DesktopScope.Work, session: SessionState.Locked);
            check(f.Engine.State == paused);
            f.Clock.UtcNow = deadline.AddTicks(-1);
            check(!f.Engine.ResumeIfPauseExpired().CheckpointRequired);
            f.Clock.UtcNow = deadline;
            var expired = f.Engine.ResumeIfPauseExpired();
            check(expired.CheckpointRequired && expired.Effects.IsEmpty);
            check(!expired.State.Paused && expired.State.PauseUntilUtc is null);
            check(expired.State.UsageMs == paused.UsageMs && expired.State.SnoozeRemainingMs == paused.SnoozeRemainingMs);
            check(expired.State.EffectSequence == paused.EffectSequence && expired.State.BreakProgressMs == 0);
            check(!f.Engine.ResumeIfPauseExpired().CheckpointRequired);
            f.Clock.UtcNow = deadline.AddDays(-1);
            check(!f.Engine.ResumeIfPauseExpired().CheckpointRequired && !f.Engine.State.Paused);
        });
        test("pause replacement manual resume reset and restart preserve intent", () =>
        {
            var f = new Fixture(); var end = f.Clock.UtcNow.AddDays(1);
            f.Engine.SetPaused(true, end);
            check(!f.Engine.SetPaused(true, end).CheckpointRequired);
            check(f.Engine.SetPaused(true, end.AddHours(1)).CheckpointRequired);
            f.Engine.ResetUsage(); check(f.Engine.State.PauseUntilUtc == end.AddHours(1));
            f.Restart(f.Engine.CreateCheckpoint()); check(f.Engine.State.PauseUntilUtc == end.AddHours(1));
            f.Clock.UtcNow = end.AddHours(2);
            check(f.Engine.ResumeIfPauseExpired().CheckpointRequired);
            f.Engine.SetPaused(true, end.AddDays(1));
            check(f.Engine.SetPaused(true).CheckpointRequired && f.Engine.State.PauseUntilUtc is null);
            f.Clock.UtcNow = end.AddYears(1); check(!f.Engine.ResumeIfPauseExpired().CheckpointRequired);
            f.Engine.SetPaused(false); check(!f.Engine.State.Paused && f.Engine.State.PauseUntilUtc is null);
        });
        test("pause deadlines validate before mutation and old intervals never earn credit", () =>
        {
            var f = new Fixture();
            foreach (var end in new[] { default(DateTimeOffset), f.Clock.UtcNow, f.Clock.UtcNow.AddMinutes(-1), f.Clock.UtcNow.AddDays(1).ToOffset(TimeSpan.FromHours(1)) })
            {
                try { f.Engine.SetPaused(true, end); throw new Exception("Accepted invalid deadline"); }
                catch (ArgumentException) { check(!f.Engine.State.Paused); }
            }
            f.Engine.SetPaused(true, f.Clock.UtcNow.AddMinutes(1));
            var interval = f.Interval(10, DesktopScope.Work);
            f.Clock.UtcNow = f.Clock.UtcNow.AddMinutes(1); f.Engine.ResumeIfPauseExpired();
            check(f.Engine.Observe(interval).Activity == ActivityKind.Uncertain);
            check(f.Engine.State.UsageMs == 0 && f.Engine.State.BreakProgressMs == 0);
        });
        test("checkpoint v4 permits expired deadlines but rejects inconsistent and legacy deadlines", () =>
        {
            var f = new Fixture(); f.Engine.SetPaused(true, f.Clock.UtcNow.AddHours(1));
            var cp = f.Engine.CreateCheckpoint(); f.Clock.UtcNow = f.Clock.UtcNow.AddHours(2);
            check(cp.IsValid(f.Clock.UtcNow));
            check(!(cp with { State = cp.State with { Paused = false } }).IsValid(f.Clock.UtcNow));
            check(!(cp with { Version = 3 }).IsValid(f.Clock.UtcNow));
        });
    }
}
