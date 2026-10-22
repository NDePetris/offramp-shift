using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;

internal static class PresentationEpochRegressions
{
    internal static void Run()
    {
        Program.Test("presentation epoch changes on reset attempts only after prerequisites", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            var before = app.Snapshot(); Program.Check(before.AccountingPresentationEpoch != Guid.Empty);
            Program.Check(!app.Execute(new(HostCommandKind.ResetUsage)).Succeeded);
            Program.Check(app.Snapshot().AccountingPresentationEpoch == before.AccountingPresentationEpoch);
            Program.Check(app.Execute(new(HostCommandKind.Pause)).Succeeded);
            Program.Check(app.Snapshot().AccountingPresentationEpoch == before.AccountingPresentationEpoch);
            Program.Check(app.Execute(new(HostCommandKind.ResetUsage) { Confirmed = true }).Succeeded);
            var after = app.Snapshot(); Program.Check(after.AccountingPresentationEpoch != before.AccountingPresentationEpoch && after.State!.UsageMs == 0);
        });
        Program.Test("ordinary pending commit keeps epoch; failed reset and every exact retry advance it", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App(); var before = app.Snapshot();
            f.Fault = stage => { if (stage == StorageFault.BeforeUpdate) throw new IOException(); };
            Program.Check(!app.Execute(new(HostCommandKind.ChangeDurations) { AllowanceMs = 123_000, RequiredBreakMs = 45_000, IdleThresholdMs = 6_000 }).Succeeded);
            var pending = app.Snapshot(); Program.Check(pending.AccountingPresentationEpoch == before.AccountingPresentationEpoch && pending.State is null);
            Program.Check(!app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
            var failed = app.Snapshot(); Program.Check(failed.AccountingPresentationEpoch != pending.AccountingPresentationEpoch && failed.State is null);
            f.Fault = null; Program.Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
            var ready = app.Snapshot(); Program.Check(ready.AccountingPresentationEpoch != failed.AccountingPresentationEpoch && ready.Configuration!.AllowanceMs == 123_000);
            Program.Check(!app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
            Program.Check(app.Snapshot().AccountingPresentationEpoch == ready.AccountingPresentationEpoch);
            f.Fault = stage => { if (stage == StorageFault.BeforeUpdate) throw new IOException(); };
            Program.Check(!app.Execute(new(HostCommandKind.ResetUsage) { Confirmed = true }).Succeeded);
            var reset = app.Snapshot(); Program.Check(reset.Persistence == PersistenceStatus.PendingCommit && reset.AccountingPresentationEpoch != ready.AccountingPresentationEpoch && reset.State is null);
        });
        Program.Test("reassignment advances presentation epoch before settings failure and session replacement", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App(); f.Advance(app, 0);
            var before = app.Snapshot(); f.Settings.BeforeReplace = () => throw new IOException();
            Program.Check(!app.Execute(new(HostCommandKind.AssignPersonal)).Succeeded);
            var failed = app.Snapshot(); Program.Check(failed.AccountingPresentationEpoch != before.AccountingPresentationEpoch && failed.Settings == before.Settings);
            f.Settings.BeforeReplace = null;
            // Explicit retry monitoring recreates the disposed observation runtime.
            Program.Check(app.Execute(new(HostCommandKind.RetryMonitoring)).Succeeded); f.Advance(app, 1_000);
            Program.Check(app.Snapshot().AccountingPresentationEpoch == failed.AccountingPresentationEpoch);
            Program.Check(app.Execute(new(HostCommandKind.AssignPersonal)).Succeeded);
            var assigned = app.Snapshot(); Program.Check(assigned.AccountingPresentationEpoch != failed.AccountingPresentationEpoch && !assigned.Settings.MonitoringEnabled);
        });
        Program.Test("blocked reopen and explicit recovery replace epochs without publishing placeholder values", () =>
        {
            using (var f = new Fixture())
            {
                f.Enable(); var utc = f.Clock.UtcNow; f.Clock.UtcNow = utc.AddDays(-1); using var app = f.App();
                var before = app.Snapshot(); Program.Check(before.Persistence == PersistenceStatus.Blocked);
                Program.Check(!app.Execute(new(HostCommandKind.Recover) { Confirmed = true }).Succeeded);
                Program.Check(app.Snapshot().AccountingPresentationEpoch == before.AccountingPresentationEpoch);
                Program.Check(app.Execute(new(HostCommandKind.ReopenStorage)).Succeeded);
                var blocked = app.Snapshot(); Program.Check(blocked.AccountingPresentationEpoch != before.AccountingPresentationEpoch && blocked.State is null);
                f.Clock.UtcNow = utc; Program.Check(app.Execute(new(HostCommandKind.ReopenStorage)).Succeeded);
                var ready = app.Snapshot(); Program.Check(ready.AccountingPresentationEpoch != blocked.AccountingPresentationEpoch && ready.Persistence == PersistenceStatus.Ready);
            }
            using (var f = new Fixture())
            {
                f.Enable(); File.WriteAllText(Path.Combine(f.PathName, "checkpoint.db"), "corrupt-test-database"); using var app = f.App();
                var before = app.Snapshot(); Program.Check(before.Persistence == PersistenceStatus.RecoveryRequired);
                Program.Check(!app.Execute(new(HostCommandKind.Recover)).Succeeded);
                Program.Check(app.Snapshot().AccountingPresentationEpoch == before.AccountingPresentationEpoch);
                Program.Check(app.Execute(new(HostCommandKind.Recover) { Confirmed = true }).Succeeded);
                var ready = app.Snapshot(); Program.Check(ready.AccountingPresentationEpoch != before.AccountingPresentationEpoch && ready.State!.UsageMs == 0);
            }
        });
        Program.Test("failed recovery invalidates before preservation; committed recovery survives later settings failure", () =>
        {
            foreach (var failSettings in new[] { false, true })
            {
                using var f = new Fixture(); f.Enable(); File.WriteAllText(Path.Combine(f.PathName, "checkpoint.db"), "corrupt-test-database"); using var app = f.App();
                var before = app.Snapshot();
                if (failSettings) f.Settings.BeforeReplace = () => throw new IOException();
                else f.Fault = stage => { if (stage == StorageFault.BeforePreservation) throw new IOException(); };
                Program.Check(!app.Execute(new(HostCommandKind.Recover) { Confirmed = true }).Succeeded);
                var after = app.Snapshot(); Program.Check(after.AccountingPresentationEpoch != before.AccountingPresentationEpoch);
                Program.Check(failSettings ? after.Persistence == PersistenceStatus.Ready && after.State!.UsageMs == 0 : after.State is null && after.Persistence == PersistenceStatus.RecoveryRequired);
            }
        });
        Program.Test("host lifetimes never share a presentation epoch", () =>
        {
            using var f = new Fixture(); Guid first;
            using (var app = f.App()) first = app.Snapshot().AccountingPresentationEpoch;
            using var second = f.App(); Program.Check(second.Snapshot().AccountingPresentationEpoch != first);
        });
    }
}
