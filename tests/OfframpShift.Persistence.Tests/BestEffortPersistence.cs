using System.Text.Json.Nodes;
using OfframpShift.Core;
using OfframpShift.Persistence;
using static Program;

internal static class BestEffortPersistence
{
    internal static void Run()
    {
        string Legacy(Fixture f)
        {
            var node = JsonNode.Parse(f.Row().Payload)!;
            node["Version"] = 1;
            var c = node["Configuration"]!.AsObject(); c.Remove("Accounting"); c.Remove("EstimationPolicyVersion");
            var s = node["State"]!.AsObject();
            foreach (var key in new[] { "EstimatedUsageMs", "BreakUncertaintyMs", "ConsecutiveUncertaintyMs" }) s.Remove(key);
            return node.ToJsonString();
        }
        Test("version-1 migration retains allowance and suppression with one atomic replacement", () =>
        {
            using var f = new Fixture();
            using (var session = f.Session()) { session.Observe(f.Clock.Interval(30)); session.Snooze(); }
            var legacy = Legacy(f); f.Payload(legacy); var revision = f.Row().Revision;
            using var migrated = f.Session();
            Equal(PersistenceStatus.Ready, migrated.Status);
            Equal(revision + 1, f.Row().Revision);
            Equal(2, CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow).Version);
            Equal(30L, migrated.State.UsageMs); Equal(0L, migrated.State.EstimatedUsageMs);
            Equal(InterventionState.Snoozed, migrated.State.Intervention);
            Equal(0, migrated.Observe(f.Clock.Interval(1)).Effects.Length);
        });
        foreach (var fault in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test($"version-1 migration {fault} blocks publication and retries the exact replacement", () =>
            {
                using var f = new Fixture();
                using (var session = f.Session()) session.Observe(f.Clock.Interval(20));
                f.Payload(Legacy(f)); var revision = f.Row().Revision;
                var active = true;
                using var migrating = f.Session(stage => { if (active && stage == fault) throw new IOException(); });
                Equal(PersistenceStatus.PendingCommit, migrating.Status);
                Fails(migrating.Failure!.Value, () => _ = migrating.State);
                active = false; migrating.RetryPendingCommit();
                Equal(PersistenceStatus.Ready, migrating.Status);
                Equal(revision + 1, f.Row().Revision); Equal(20L, migrating.State.UsageMs);
                Equal(0, migrating.Observe(f.Clock.Interval(1)).Effects.Length);
            });
        Test("version-1 payload rejects new fields and malformed migration never writes", () =>
        {
            using var f = new Fixture(); f.Initialize();
            var legacy = JsonNode.Parse(Legacy(f))!;
            legacy["State"]!["EstimatedUsageMs"] = 0;
            f.Payload(legacy.ToJsonString()); var before = f.Row();
            using var blocked = f.Session();
            Equal(PersistenceStatus.RecoveryRequired, blocked.Status); Equal(before, f.Row());
        });
        Test("acknowledged migration with restore failure retries without a second replacement", () =>
        {
            using var f = new Fixture(); f.Initialize(); f.Payload(Legacy(f));
            var revision = f.Row().Revision; var committed = false;
            f.Clock.UtcRead = value => committed ? throw new InvalidOperationException() : value;
            using var migrating = f.Session(stage => { if (stage == StorageFault.AfterCommit) committed = true; });
            Equal(PersistenceStatus.PendingCommit, migrating.Status);
            Equal(revision + 1, f.Row().Revision); var row = f.Row();
            f.Clock.UtcRead = null; migrating.RetryPendingCommit();
            Equal(PersistenceStatus.Ready, migrating.Status); Equal(row, f.Row());
        });
        Test("estimated totals persist and failure retry abandons break uncertainty without replay", () =>
        {
            using var f = new Fixture(); var fail = false;
            using var session = f.Session(stage => { if (fail && stage == StorageFault.AfterCommit) throw new IOException(); });
            session.ChangeConfiguration(Fixture.Small with { Accounting = AccountingMode.BestEffort });
            fail = true;
            Fails(StorageFailure.UncertainCommit, () => session.Observe(f.Clock.Interval(30, confidence: EvidenceConfidence.Estimated)));
            fail = false; session.RetryPendingCommit();
            Equal(30L, session.State.EstimatedUsageMs); Equal(30L, session.State.UsageMs);
            Equal(0, session.Observe(f.Clock.Interval(1, confidence: EvidenceConfidence.Estimated)).Effects.Length);
        });
        Test("future estimation policy is incompatible rather than destructive recovery", () =>
        {
            using var f = new Fixture(); f.Initialize();
            var node = JsonNode.Parse(f.Row().Payload)!;
            node["Configuration"]!["EstimationPolicyVersion"] = 2;
            f.Payload(node.ToJsonString()); var before = f.Row();
            using var blocked = f.Session();
            Equal(PersistenceStatus.Incompatible, blocked.Status); Equal(before, f.Row());
            try { blocked.Reinitialize(Fixture.Small); throw new Exception("Recovery must remain unavailable."); }
            catch (InvalidOperationException) { }
        });
        Test("migration then selecting best-effort preserves the completed reset latch", () =>
        {
            using var f = new Fixture();
            using (var s = f.Session()) { s.Observe(f.Clock.Interval(30)); s.Observe(f.Clock.Interval(15, DesktopScope.Work)); }
            f.Payload(Legacy(f));
            using var migrated = f.Session(); var sequence = migrated.State.EffectSequence;
            True(migrated.State.BreakCompleted);
            migrated.ChangeConfiguration(migrated.Configuration with { Accounting = AccountingMode.BestEffort });
            True(migrated.State.BreakCompleted);
            Equal(0, migrated.Observe(f.Clock.Interval(1, DesktopScope.Work, confidence: EvidenceConfidence.Estimated)).Effects.Length);
            Equal(sequence, migrated.State.EffectSequence);
        });
    }
}
