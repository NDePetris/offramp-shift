using OfframpShift.Core;
using OfframpShift.Persistence;
using static Program;

internal static class ResetUsagePersistence
{
    internal static void Run()
    {
        Test("ordinary reset is durable without changing schema configuration or Pause", () =>
        {
            using var f = new Fixture(); long sequence;
            using (var session = f.Session())
            {
                session.Observe(f.Clock.Interval(36)); session.SetPaused(true);
                sequence = session.State.EffectSequence;
                var result = session.ResetUsage();
                Equal(0, result.Effects.Length); Equal(0L, session.State.UsageMs);
                Equal(sequence, session.State.EffectSequence); True(session.State.Paused);
            }
            using var restored = f.Session();
            Equal(System.Text.Json.JsonSerializer.Serialize(Fixture.Small), System.Text.Json.JsonSerializer.Serialize(restored.Configuration));
            Equal(0L, restored.State.UsageMs);
            True(restored.State.Paused); Equal(sequence, restored.State.EffectSequence);
            Equal(4, CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow).Version);
        });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test("reset failed save retains exact pending checkpoint: " + stage, () =>
            {
                using var f = new Fixture(); var active = false;
                using var session = f.Session(point => { if (active && point == stage) throw new IOException(); });
                session.Observe(f.Clock.Interval(36)); session.Observe(f.Clock.Interval(5, DesktopScope.Work));
                var before = f.Row(); var sequence = session.State.EffectSequence;
                active = true;
                var failure = stage == StorageFault.AfterCommit ? StorageFailure.UncertainCommit : StorageFailure.AccessFailure;
                Fails(failure, () => session.ResetUsage());
                Equal(PersistenceStatus.PendingCommit, session.Status);
                Fails(failure, () => _ = session.State);
                Fails(failure, () => session.ResetUsage());
                if (stage != StorageFault.AfterCommit) Equal(before, f.Row());
                else Equal(0L, CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow).State.UsageMs);
                active = false; f.Clock.Now = new(Guid.NewGuid(), 10_000);
                f.Clock.UtcNow = f.Clock.UtcNow.AddHours(1);
                session.RetryPendingCommit();
                Equal(PersistenceStatus.Ready, session.Status);
                Equal(0L, session.State.UsageMs); Equal(0L, session.State.BreakProgressMs);
                True(!session.State.BreakCompleted && !session.State.Exhausted);
                Equal(sequence, session.State.EffectSequence);
                Equal(before.Revision + 1, f.Row().Revision);
                Equal(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow).SavedUtc);
                Equal(0, session.Observe(f.Clock.Interval(1)).Effects.Length);
                Equal(1L, session.State.UsageMs);
            });
        Test("crash before uncommitted reset retains the previous saved accounting", () =>
        {
            using var f = new Fixture(); var active = false;
            using (var session = f.Session(point => { if (active && point == StorageFault.BeforeUpdate) throw new IOException(); }))
            {
                session.Observe(f.Clock.Interval(36)); active = true;
                Fails(StorageFailure.AccessFailure, () => session.ResetUsage());
            }
            using var restored = f.Session();
            Equal(36L, restored.State.UsageMs); True(restored.State.Exhausted);
        });
    }
}
