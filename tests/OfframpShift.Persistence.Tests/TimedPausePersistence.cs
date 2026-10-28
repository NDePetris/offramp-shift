using System.Text.Json.Nodes;
using OfframpShift.Core;
using OfframpShift.Persistence;
using static Program;

internal static class TimedPausePersistence
{
    internal static void Run()
    {
        Test("invalid Pause selections leave ready storage and the entire checkpoint unchanged", () =>
        {
            using var f = new Fixture(); using var s = f.Session();
            s.Observe(f.Clock.Interval(5)); s.SetPaused(true, f.Clock.UtcNow.AddHours(1));
            var before = s.State; var row = f.Row();
            foreach (var (paused, end) in new[] {
                (true, default(DateTimeOffset)), (true, f.Clock.UtcNow), (true, f.Clock.UtcNow.AddTicks(-1)),
                (true, f.Clock.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(1))), (false, f.Clock.UtcNow.AddHours(1)) })
            {
                try { s.SetPaused(paused, end); throw new InvalidOperationException("Invalid selection accepted."); }
                catch (PauseSelectionRejectedException) { }
                Equal(PersistenceStatus.Ready, s.Status); True(s.Failure is null);
                Equal(before, s.State); Equal(row, f.Row());
            }
            // The rejected command did not leave the session busy or prevent a save.
            s.SetPaused(false); True(!s.State.Paused); Equal(row.Revision + 1, f.Row().Revision);
        });
        Test("selection exception during checkpoint capture still blocks a partial Pause transition", () =>
        {
            using var f = new Fixture(); using var s = f.Session();
            PauseSelectionRejectedException? rejection = null;
            try { s.SetPaused(true, f.Clock.UtcNow); }
            catch (PauseSelectionRejectedException e) { rejection = e; }
            True(rejection is not null); var row = f.Row();
            // Indefinite Pause performs no UTC selection read, so this fault occurs
            // only after SetPaused has mutated Core, during checkpoint capture.
            f.Clock.UtcRead = _ => throw rejection!;
            Fails(StorageFailure.UnexpectedFailure, () => s.SetPaused(true));
            f.Clock.UtcRead = null;
            Equal(PersistenceStatus.Blocked, s.Status); Equal(row, f.Row());
            Fails(StorageFailure.UnexpectedFailure, () => _ = s.State);
        });
        Test("invalid monotonic clock during a valid Pause selection still blocks storage", () =>
        {
            using var f = new Fixture(); using var s = f.Session(); var row = f.Row();
            f.Clock.Now = f.Clock.Now with { Milliseconds = -1 };
            Fails(StorageFailure.UnexpectedFailure, () => s.SetPaused(true, f.Clock.UtcNow.AddMinutes(1)));
            Equal(PersistenceStatus.Blocked, s.Status); Equal(row, f.Row());
        });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test($"v3 to v4 migration at {stage} retains exact payload and original save time", () =>
            {
                using var f = new Fixture(); f.Initialize();
                var node = JsonNode.Parse(f.Row().Payload)!; node["Version"] = 3;
                node["State"]!.AsObject().Remove("PauseUntilUtc"); f.Payload(node.ToJsonString());
                var before = f.Row(); var savedUtc = f.Clock.UtcNow; var fail = true;
                using var s = f.Session(point => { if (fail && point == stage) throw new IOException(); });
                Equal(PersistenceStatus.PendingCommit, s.Status);
                fail = false; f.Clock.UtcNow = savedUtc.AddHours(1); s.RetryPendingCommit();
                Equal(before.Revision + 1, f.Row().Revision);
                var cp = CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow);
                Equal(savedUtc, cp.SavedUtc); Equal(4, cp.Version); True(cp.State.PauseUntilUtc is null);
            });
        foreach (var version in new[] { 1, 2, 3 })
            foreach (var paused in new[] { false, true })
                Test($"v{version} migration preserves {(paused ? "indefinite pause" : "active state")} in v4", () =>
                {
                    using var f = new Fixture();
                    using (var s = f.Session()) { s.Observe(f.Clock.Interval(5)); s.SetPaused(paused); }
                    var node = JsonNode.Parse(f.Row().Payload)!; node["Version"] = version;
                    var state = node["State"]!.AsObject(); state.Remove("PauseUntilUtc");
                    if (version < 3) node["Configuration"]!.AsObject().Remove("InteractiveRepeatNudgeMs");
                    if (version == 1)
                    {
                        foreach (var key in new[] { "Accounting", "EstimationPolicyVersion" }) node["Configuration"]!.AsObject().Remove(key);
                        foreach (var key in new[] { "EstimatedUsageMs", "BreakUncertaintyMs", "ConsecutiveUncertaintyMs" }) state.Remove(key);
                    }
                    f.Payload(node.ToJsonString()); var before = f.Row();
                    using var migrated = f.Session();
                    Equal(PersistenceStatus.Ready, migrated.Status); Equal(before.Revision + 1, f.Row().Revision);
                    Equal(paused, migrated.State.Paused); True(migrated.State.PauseUntilUtc is null);
                    Equal(5L, migrated.State.UsageMs);
                    var checkpoint = CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow);
                    Equal(4, checkpoint.Version);
                });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            foreach (var operation in new[] { "pause", "expiry", "manual" })
                Test($"timed {operation} at {stage} retries exact state without replay", () =>
                {
                    using var f = new Fixture(); var fail = false;
                    using var s = f.Session(point => { if (fail && point == stage) throw new IOException(); });
                    s.Observe(f.Clock.Interval(5)); var deadline = f.Clock.UtcNow.AddMinutes(1);
                    if (operation != "pause") s.SetPaused(true, deadline);
                    var before = f.Row();
                    if (operation == "expiry") f.Clock.UtcNow = deadline;
                    var capturedUtc = f.Clock.UtcNow; fail = true;
                    Fails(stage == StorageFault.AfterCommit ? StorageFailure.UncertainCommit : StorageFailure.AccessFailure, () =>
                    {
                        if (operation == "pause") s.SetPaused(true, deadline);
                        else if (operation == "manual") s.SetPaused(false);
                        else s.ResumeIfPauseExpired();
                    });
                    Equal(PersistenceStatus.PendingCommit, s.Status);
                    if (stage != StorageFault.AfterCommit) Equal(before, f.Row());
                    fail = false; f.Clock.UtcNow = deadline.AddHours(1); s.RetryPendingCommit();
                    Equal(before.Revision + 1, f.Row().Revision);
                    var committed = CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow);
                    Equal(capturedUtc, committed.SavedUtc); Equal(5L, s.State.UsageMs);
                    Equal(operation == "pause", s.State.Paused);
                    Equal(operation == "pause" ? deadline : (DateTimeOffset?)null, s.State.PauseUntilUtc);
                    var result = s.ResumeIfPauseExpired(); True(result.Effects.IsEmpty);
                    True(!s.State.Paused && s.State.PauseUntilUtc is null);
                });
        foreach (var after in new[] { false, true })
            Test($"restart {(after ? "after" : "before")} pause deadline never earns downtime credit", () =>
            {
                using var f = new Fixture(); var end = f.Clock.UtcNow.AddHours(1);
                using (var s = f.Session()) { s.Observe(f.Clock.Interval(5)); s.SetPaused(true, end); }
                f.Clock.UtcNow = after ? end : end.AddTicks(-1); f.Clock.Now = new(Guid.NewGuid(), 100_000);
                using var restored = f.Session(); Equal(end, restored.State.PauseUntilUtc);
                Equal(after, restored.ResumeIfPauseExpired().CheckpointRequired);
                Equal(5L, restored.State.UsageMs); Equal(0L, restored.State.BreakProgressMs);
            });
        Test("v4 strict deadline shape and legacy extra fields are rejected", () =>
        {
            using var f = new Fixture(); f.Initialize(); var valid = f.Row().Payload;
            foreach (var mode in new[] { "missing", "duplicate", "malformed", "offset", "active", "legacy", "future" })
            {
                var node = JsonNode.Parse(valid)!; var state = node["State"]!.AsObject();
                if (mode == "missing") state.Remove("PauseUntilUtc");
                if (mode == "malformed") state["PauseUntilUtc"] = "tomorrow";
                if (mode == "offset") { state["Paused"] = true; state["PauseUntilUtc"] = "2026-10-14T12:00:00+01:00"; }
                if (mode == "active") state["PauseUntilUtc"] = "2026-10-14T12:00:00Z";
                if (mode == "legacy") node["Version"] = 3;
                if (mode == "future") node["Version"] = 5;
                var payload = node.ToJsonString();
                if (mode == "duplicate") payload = payload.Replace("\"PauseUntilUtc\":null", "\"PauseUntilUtc\":null,\"PauseUntilUtc\":null", StringComparison.Ordinal);
                Fails(mode == "future" ? StorageFailure.UnsupportedVersion : StorageFailure.InvalidPayload,
                    () => CheckpointCodec.Decode(payload, f.Clock.UtcNow));
            }
        });
    }
}
