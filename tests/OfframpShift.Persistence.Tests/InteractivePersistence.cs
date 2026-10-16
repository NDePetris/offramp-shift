using System.Text.Json.Nodes;
using OfframpShift.Core;
using OfframpShift.Persistence;
using static Program;

internal static class InteractivePersistence
{
    internal static void Run()
    {
        Test("v2 migration preserves legacy state and initializes interactive cadence only", () =>
        {
            using var f = new Fixture();
            using (var session = f.Session()) { session.Observe(f.Clock.Interval(36)); session.Snooze(); }
            var node = JsonNode.Parse(f.Row().Payload)!; node["Version"] = 2;
            node["Configuration"]!.AsObject().Remove("InteractiveRepeatNudgeMs");
            f.Payload(node.ToJsonString()); var old = f.Row();
            using var migrated = f.Session();
            Equal(PersistenceStatus.Ready, migrated.Status); Equal(old.Revision + 1, f.Row().Revision);
            Equal(60_000L, migrated.Configuration.InteractiveRepeatNudgeMs);
            Equal(10L, migrated.Configuration.RepeatNudgeMs); Equal(6L, migrated.Configuration.GraceMs);
            Equal(InterventionState.Snoozed, migrated.State.Intervention); Equal(10L, migrated.State.SnoozeRemainingMs);
        });
        foreach (var duration in new[] { 60_000L, 300_000L })
            Test($"Personal deferral {duration} persists without downtime credit or replay", () =>
            {
                using var f = new Fixture();
                using (var session = f.Session())
                { session.Observe(f.Clock.Interval(30), true); session.DeferInteractive(duration); }
                f.Clock.Now = new(Guid.NewGuid(), 1_000_000);
                using var restored = f.Session();
                Equal(InterventionState.PersonalDeferred, restored.State.Intervention);
                Equal(duration, restored.State.SnoozeRemainingMs);
                Equal(0, restored.Observe(f.Clock.Interval(1), true).Effects.Length);
                Equal(duration - 1, restored.State.SnoozeRemainingMs);
                True(!restored.DeferInteractive(duration).CheckpointRequired);
            });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test($"interactive failed commit {stage} retries exact duration once", () =>
            {
                using var f = new Fixture(); var fail = false;
                using var session = f.Session(point => { if (fail && point == stage) throw new IOException(); });
                session.Observe(f.Clock.Interval(30), true); fail = true;
                Fails(stage == StorageFault.AfterCommit ? StorageFailure.UncertainCommit : StorageFailure.AccessFailure,
                    () => session.DeferInteractive(300_000));
                Equal(PersistenceStatus.PendingCommit, session.Status);
                fail = false; session.RetryPendingCommit();
                Equal(300_000L, session.State.SnoozeRemainingMs);
                Equal(0, session.Observe(f.Clock.Interval(1), true).Effects.Length);
            });
        Test("v3 strict cadence and deferral invariants reject malformed and future checkpoints", () =>
        {
            using var f = new Fixture(); f.Initialize(); var valid = f.Row().Payload;
            foreach (var mode in new[] { "missing", "fractional", "future", "old-state" })
            {
                var node = JsonNode.Parse(valid)!;
                if (mode == "missing") node["Configuration"]!.AsObject().Remove("InteractiveRepeatNudgeMs");
                if (mode == "fractional") node["Configuration"]!["InteractiveRepeatNudgeMs"] = 60_001;
                if (mode == "future") node["Version"] = 4;
                if (mode == "old-state") { node["Version"] = 2; node["Configuration"]!.AsObject().Remove("InteractiveRepeatNudgeMs"); node["State"]!["Intervention"] = 4; }
                Fails(mode == "future" ? StorageFailure.UnsupportedVersion : StorageFailure.InvalidPayload,
                    () => CheckpointCodec.Decode(node.ToJsonString(), f.Clock.UtcNow));
            }
        });
    }
}
