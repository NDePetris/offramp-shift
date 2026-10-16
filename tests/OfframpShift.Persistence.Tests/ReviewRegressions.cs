using OfframpShift.Core;
using OfframpShift.Persistence;
using static Program;

internal static class ReviewRegressions
{
    internal static void Run()
    {
        Test("reentrant clock getters cannot publish state, issue commands or release the lease", () =>
        {
            using var f = new Fixture();
            using var session = f.Session();
            f.Clock.UtcRead = value =>
            {
                Fails(StorageFailure.Busy, () => _ = session.State);
                Fails(StorageFailure.Busy, () => session.SetPaused(true));
                Fails(StorageFailure.Busy, session.Dispose);
                return value;
            };
            session.Observe(f.Clock.Interval(30));
            f.Clock.UtcRead = null;
            Equal(PersistenceStatus.Ready, session.Status);
            Equal(30L, session.State.UsageMs);
            Equal(2L, f.Row().Revision);
        });
        Test("unexpected checkpoint-capture failure cannot publish mutated Ready state", () =>
        {
            using var f = new Fixture();
            using (var session = f.Session())
            {
                var before = f.Row();
                f.Clock.UtcRead = _ => throw new InvalidOperationException("private clock diagnostic");
                Fails(StorageFailure.UnexpectedFailure, () => session.Observe(f.Clock.Interval(30)));
                Equal(PersistenceStatus.Blocked, session.Status);
                Fails(StorageFailure.UnexpectedFailure, () => _ = session.State);
                Fails(StorageFailure.UnexpectedFailure, () => session.SetPaused(true));
                Fails(StorageFailure.UnexpectedFailure, () => session.Snooze());
                Fails(StorageFailure.UnexpectedFailure, () => session.ChangeConfiguration(Fixture.Small));
                Equal(before, f.Row());
            }
            f.Clock.UtcRead = null;
            using var restart = f.Session();
            Equal(0L, restart.State.UsageMs);
        });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test($"unexpected exception at {stage} retains exact pending payload", () =>
            {
                using var f = new Fixture();
                var active = false;
                using var session = f.Session(point => { if (active && point == stage) throw new InvalidOperationException("private detail"); });
                active = true;
                var category = stage == StorageFault.AfterCommit ? StorageFailure.UncertainCommit : StorageFailure.UnexpectedFailure;
                Fails(category, () => session.Observe(f.Clock.Interval(30)));
                Equal(PersistenceStatus.PendingCommit, session.Status);
                Fails(category, () => _ = session.State);
                Fails(category, () => session.Observe(f.Clock.Interval(2)));
                active = false;
                f.Clock.Now = new(Guid.NewGuid(), 100_000);
                f.Clock.UtcNow = f.Clock.UtcNow.AddHours(2);
                session.RetryPendingCommit();
                Equal(30L, session.State.UsageMs);
                Equal(2L, f.Row().Revision);
                Equal(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow).SavedUtc);
                Equal(0, session.Observe(f.Clock.Interval(1)).Effects.Length);
                Equal(31L, session.State.UsageMs);
            });
        Test("backward UTC at startup blocks without permitting destructive recovery", () =>
        {
            using var f = new Fixture();
            using (var s = f.Session()) s.Observe(f.Clock.Interval(20));
            var before = f.Row();
            f.Clock.UtcNow = f.Clock.UtcNow.AddDays(-1);
            using (var blocked = f.Session())
            {
                Equal(PersistenceStatus.Blocked, blocked.Status);
                Equal(StorageFailure.ClockUnavailable, blocked.Failure);
                Fails(StorageFailure.ClockUnavailable, () => blocked.SetPaused(true));
                try { blocked.Reinitialize(Fixture.Small); throw new Exception("Recovery must be unavailable."); }
                catch (InvalidOperationException) { }
                Equal(before, f.Row());
            }
            f.Clock.UtcNow = f.Clock.UtcNow.AddDays(2);
            f.Clock.Now = new(Guid.NewGuid(), 1_000_000);
            using var restored = f.Session();
            Equal(PersistenceStatus.Ready, restored.Status);
            Equal(20L, restored.State.UsageMs);
        });
        Test("backward UTC during save remains an exact non-destructive pending retry", () =>
        {
            using var f = new Fixture();
            using var session = f.Session();
            session.Observe(f.Clock.Interval(5));
            f.Clock.UtcNow = f.Clock.UtcNow.AddHours(-1);
            var pendingUtc = f.Clock.UtcNow;
            Fails(StorageFailure.ClockUnavailable, () => session.Observe(f.Clock.Interval(2)));
            Equal(PersistenceStatus.PendingCommit, session.Status);
            f.Clock.UtcNow = f.Clock.UtcNow.AddHours(2);
            session.RetryPendingCommit();
            Equal(7L, session.State.UsageMs);
            Equal(pendingUtc, CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow).SavedUtc);
        });
        Test("UTC changes between load validation and Restore never expose recovery as Ready", () =>
        {
            using var f = new Fixture();
            f.Initialize();
            var reads = 0;
            f.Clock.UtcRead = value => ++reads <= 1 ? value : value.AddDays(-1);
            using var session = f.Session();
            Equal(PersistenceStatus.Blocked, session.Status);
            Equal(StorageFailure.ClockUnavailable, session.Failure);
            Fails(StorageFailure.ClockUnavailable, () => _ = session.State);
        });
        Test("invalid state with future UTC remains invalid rather than a clock-only problem", () =>
        {
            using var f = new Fixture();
            var invalid = f.Checkpoint with { SavedUtc = f.Clock.UtcNow.AddDays(1), State = f.Checkpoint.State with { UsageMs = -1 } };
            Fails(StorageFailure.InvalidPayload, () => CheckpointCodec.Decode(System.Text.Json.JsonSerializer.Serialize(invalid), f.Clock.UtcNow));
        });
        Test("restore failure after retry acknowledgment never saves the checkpoint twice", () =>
        {
            using var f = new Fixture();
            var active = false;
            using var session = f.Session(point => { if (active && point == StorageFault.AfterCommit) throw new IOException(); });
            active = true;
            Fails(StorageFailure.UncertainCommit, () => session.Observe(f.Clock.Interval(30)));
            active = false;
            var reads = 0;
            f.Clock.UtcRead = value => ++reads == 3 ? throw new InvalidOperationException() : value;
            Fails(StorageFailure.UnexpectedFailure, session.RetryPendingCommit);
            Equal(PersistenceStatus.PendingCommit, session.Status);
            Equal(2L, f.Row().Revision);
            var acknowledged = f.Row();
            f.Clock.UtcRead = null;
            session.RetryPendingCommit();
            Equal(acknowledged, f.Row());
            Equal(30L, session.State.UsageMs);
        });
        Test("future domain hidden behind journal cannot be overwritten during recovery", () =>
        {
            using var f = new Fixture();
            f.Initialize();
            f.Payload("{\"Version\":4,\"FutureLayout\":true}");
            File.WriteAllText(f.FilePath("initialized"), "bad");
            File.WriteAllText(f.FilePath("checkpoint.db-journal"), "companion");
            var original = File.ReadAllBytes(f.FilePath("checkpoint.db"));
            using var session = f.Session();
            Fails(StorageFailure.UnsupportedVersion, () => session.Reinitialize(Fixture.Small));
            Equal(PersistenceStatus.Incompatible, session.Status);
            True(original.SequenceEqual(File.ReadAllBytes(f.FilePath("checkpoint.db"))));
            Equal("bad", File.ReadAllText(f.FilePath("initialized")));
            Equal("companion", File.ReadAllText(f.FilePath("checkpoint.db-journal")));
        });
        Test("future storage header with damaged schema cannot be overwritten", () =>
        {
            using var f = new Fixture();
            f.Initialize();
            f.Sql("PRAGMA user_version=2; PRAGMA writable_schema=ON; UPDATE sqlite_schema SET sql='malformed' WHERE name='checkpoint'");
            File.WriteAllText(f.FilePath("initialized"), "bad");
            var original = File.ReadAllBytes(f.FilePath("checkpoint.db"));
            using var session = f.Session();
            Fails(StorageFailure.UnsupportedVersion, () => session.Reinitialize(Fixture.Small));
            Equal(PersistenceStatus.Incompatible, session.Status);
            True(original.SequenceEqual(File.ReadAllBytes(f.FilePath("checkpoint.db"))));
        });
        Test("unexpected recovery exception stays blocked and remains explicitly recoverable", () =>
        {
            using var f = new Fixture();
            f.Initialize();
            File.WriteAllText(f.FilePath("initialized"), "bad");
            var active = true;
            using var session = f.Session(point => { if (active && point == StorageFault.AfterPreservation) throw new InvalidOperationException(); });
            Fails(StorageFailure.UnexpectedFailure, () => session.Reinitialize(Fixture.Small));
            Equal(PersistenceStatus.RecoveryRequired, session.Status);
            Fails(StorageFailure.InterruptedRecovery, () => session.Observe(f.Clock.Interval(1)));
            active = false;
            session.Reinitialize(Fixture.Small);
            Equal(PersistenceStatus.Ready, session.Status);
            Equal(2, Directory.GetDirectories(f.DirectoryPath, "recovery-*").Length);
        });
    }
}
