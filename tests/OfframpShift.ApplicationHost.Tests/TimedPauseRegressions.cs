using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;
using static Program;

internal static class TimedPauseRegressions
{
    internal static void Run()
    {
        foreach (var previousPause in new[] { "active", "indefinite", "timed" })
            Test("deadline crossing between host and Core validation preserves confirmed state: " + previousPause, () =>
            {
                using var f = new Fixture(); f.Enable();
                using (var session = f.Session())
                {
                    var start = f.Clock.Now; f.Clock.Now = start with { Milliseconds = 100 };
                    session.Observe(new(start, f.Clock.Now, DesktopScope.Personal,
                        SessionState.Active, new("test", true), InputState.Active, MonitoringAvailability.Available, EvidenceConfidence.Confirmed));
                }
                using var app = f.App();
                if (previousPause != "active")
                    Check(app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = previousPause == "timed" ? f.Clock.UtcNow.AddHours(1) : null }).Succeeded);
                var before = app.Snapshot();
                var checkpointPath = Path.Combine(f.PathName, SqliteCheckpointStore.DatabaseName);
                var checkpoint = File.ReadAllBytes(checkpointPath);
                var end = f.Clock.UtcNow.AddMinutes(1); var reads = 0;
                f.Clock.UtcRead = _ => ++reads == 1 ? end.AddTicks(-1) : end;
                var result = app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = end });
                f.Clock.UtcRead = null;
                var after = app.Snapshot();
                Check(reads == 2 && !result.Succeeded && result.Message.Contains("future pause end time", StringComparison.Ordinal));
                Check(after.Persistence == PersistenceStatus.Ready && after.StorageFailure is null);
                Check(after.State == before.State && after.Configuration == before.Configuration);
                Check(checkpoint.SequenceEqual(File.ReadAllBytes(checkpointPath)));
            });
        foreach (var condition in new[] { "Personal", "Work", "Other", "Idle", "Locked", "Suspended", "Fault", "Missing", "Disabled", "Unsupported" })
            Test("pause expiry is independent of observation: " + condition, () =>
            {
                using var f = new Fixture(); f.Enable();
                if (condition == "Disabled") f.Settings.Save(f.Settings.Load() with { MonitoringEnabled = false });
                using var app = f.App(supported: condition != "Unsupported");
                var end = f.Clock.UtcNow.AddMinutes(1);
                Check(app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = end }).Succeeded);
                if (f.Source is { } source)
                {
                    if (condition == "Work") source.Current = f.Work;
                    if (condition == "Other") source.Current = Guid.NewGuid();
                    if (condition == "Idle") source.IdleAgeMs = 100_000;
                    if (condition == "Locked") source.Session = SessionState.Locked;
                    if (condition == "Suspended") source.Session = SessionState.Suspended;
                    if (condition == "Missing") source.Topology = [f.Personal];
                    if (condition == "Fault") { source.FailRead = true; f.Advance(app, 1_000); }
                }
                f.Clock.UtcNow = end.AddTicks(-1); app.Step(); Check(app.Snapshot().State!.Paused);
                f.Clock.UtcNow = end; f.Advance(app, 100_000);
                var state = app.Snapshot().State!;
                Check(!state.Paused && state.PauseUntilUtc is null);
                Check(state.UsageMs == 0 && state.BreakProgressMs == 0 && state.EffectSequence == 0);
                Check(!app.Reminders.TryTake(Environment.TickCount64));
                app.Step(); Check(!app.Snapshot().State!.Paused);
            });
        foreach (var expired in new[] { false, true })
            Test("startup reconciles saved timed pause: " + expired, () =>
            {
                using var f = new Fixture(); f.Enable(); var end = f.Clock.UtcNow.AddHours(1);
                using (var s = f.Session()) s.SetPaused(true, end);
                f.Clock.UtcNow = expired ? end : end.AddTicks(-1);
                f.FailCreate = true; using var app = f.App();
                Check(app.Snapshot().State!.Paused != expired);
                Check(app.Snapshot().State!.UsageMs == 0);
            });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test("expiry failure remains actionable and retries exact transition: " + stage, () =>
            {
                using var f = new Fixture(); f.Enable(); using var app = f.App();
                var end = f.Clock.UtcNow.AddMinutes(1);
                Check(app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = end }).Succeeded);
                f.Fault = point => { if (point == stage) throw new IOException(); };
                f.Clock.UtcNow = end; app.Step();
                var pending = app.Snapshot();
                Check(pending.Persistence == PersistenceStatus.PendingCommit && pending.State is null);
                Check(pending.PauseRecoveryDetail.Contains("Retry save", StringComparison.Ordinal));
                app.Step(); Check(app.Snapshot().Persistence == PersistenceStatus.PendingCommit);
                f.Fault = null; f.Clock.UtcNow = end.AddHours(1);
                Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
                Check(!app.Snapshot().State!.Paused && app.Snapshot().PauseRecoveryDetail.Length == 0);
                Check(app.Snapshot().State!.UsageMs == 0 && app.Snapshot().State!.BreakProgressMs == 0);
            });
        Test("pending pause acknowledged after deadline reconciles before publication", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            var end = f.Clock.UtcNow.AddMinutes(1);
            f.Fault = point => { if (point == StorageFault.AfterCommit) throw new IOException(); };
            Check(!app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = end }).Succeeded);
            f.Clock.UtcNow = end; f.Fault = null;
            Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
            Check(!app.Snapshot().State!.Paused);
        });
        Test("blocked expiry exposes recovery action and storage reopen reconciles before publication", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            var end = f.Clock.UtcNow.AddMinutes(1);
            Check(app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = end }).Succeeded);
            f.Clock.Now = f.Clock.Now with { Milliseconds = -1 }; f.Clock.UtcNow = end;
            app.Step();
            Check(app.Snapshot().Persistence == PersistenceStatus.Blocked);
            Check(app.Snapshot().PauseRecoveryDetail.Contains("Resolve accounting storage", StringComparison.Ordinal));
            f.Clock.Now = new(Guid.NewGuid(), 0);
            Check(app.Execute(new(HostCommandKind.ReopenStorage)).Succeeded);
            Check(!app.Snapshot().State!.Paused && app.Snapshot().State!.PauseUntilUtc is null);
        });
        Test("Resume now works with unavailable providers and expired selections do not mutate storage", () =>
        {
            using var f = new Fixture(); f.Enable(); f.FailCreate = true; using var app = f.App();
            var end = f.Clock.UtcNow.AddMinutes(1);
            Check(app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = end }).Succeeded);
            Check(app.Execute(new(HostCommandKind.Resume)).Succeeded);
            Check(!app.Snapshot().State!.Paused && app.Snapshot().State!.PauseUntilUtc is null);
            Check(!app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = f.Clock.UtcNow }).Succeeded);
            Check(app.Snapshot().Persistence == PersistenceStatus.Ready && !app.Snapshot().State!.Paused);
        });
        Test("wall clock correction and sleep epoch never extend or replay accounted intervals", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            var end = f.Clock.UtcNow.AddHours(1);
            Check(app.Execute(new(HostCommandKind.Pause) { PauseUntilUtc = end }).Succeeded);
            f.Clock.UtcNow = end.AddHours(-2); f.Advance(app, 1_000_000); Check(app.Snapshot().State!.Paused);
            f.Clock.Now = new(Guid.NewGuid(), 0); f.Clock.UtcNow = end.AddHours(1); app.Step();
            Check(!app.Snapshot().State!.Paused && app.Snapshot().State!.UsageMs == 0);
            f.Advance(app, 1_000); Check(app.Snapshot().State!.UsageMs == 0);
            f.Clock.UtcNow = end.AddHours(-2); app.Step(); Check(!app.Snapshot().State!.Paused);
        });
    }
}
