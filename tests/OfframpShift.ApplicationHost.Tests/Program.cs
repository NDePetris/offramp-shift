using System.Text.Json;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;

internal static class Program
{
    private static int passed, failed, assertions;
    internal static void Check(bool condition) { assertions++; if (!condition) throw new InvalidOperationException("Assertion failed"); }
    internal static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL: " + name + " " + e.Message); }
    }
    public static int Main(string[] args)
    {
        if (args is [var mode] && mode.StartsWith("--switch-fault-", StringComparison.Ordinal)) return SwitchRegressions.Fault(mode);
        Test("setup snapshots progress through consent individual assignments and explicit start", () =>
        {
            using var f = new Fixture(); using var app = f.App();
            var initial = app.Snapshot();
            Check(initial.CanConsent && !initial.CanAssign && !initial.ObservationAllowed);
            Check(initial.PersonalAssignment == AssignmentStatus.Unassigned && initial.WorkAssignment == AssignmentStatus.Unassigned);
            Check(!app.Execute(new(HostCommandKind.AssignPersonal)).Succeeded);
            Check(app.Execute(new(HostCommandKind.Consent) { Confirmed = true }).Succeeded);
            f.Advance(app, 0); Check(app.Snapshot().CanAssign && !app.Snapshot().CanConsent);
            Check(app.Execute(new(HostCommandKind.AssignPersonal)).Succeeded);
            f.Advance(app, 1_000);
            Check(app.Snapshot().PersonalAssignment == AssignmentStatus.Valid && app.Snapshot().WorkAssignment == AssignmentStatus.Unassigned);
            var before = app.Snapshot().Settings;
            var duplicate = app.Execute(new(HostCommandKind.AssignWork));
            Check(!duplicate.Succeeded && duplicate.Message.Contains("Task View"));
            Check(app.Snapshot().Settings == before);
            f.Source!.Current = f.Work;
            Check(app.Execute(new(HostCommandKind.AssignWork)).Succeeded);
            f.Advance(app, 2_000); Check(app.Snapshot().BindingsValid && !app.Snapshot().Settings.MonitoringEnabled);
            Check(app.Execute(new(HostCommandKind.Start)).Succeeded);
            f.Source!.Topology = [f.Personal]; f.Advance(app, 3_000);
            Check(app.Snapshot().WorkAssignment == AssignmentStatus.Missing);
            Check(!app.Execute(new(HostCommandKind.Start)).Succeeded);
            f.Source.Current = Guid.NewGuid();
            var invalid = app.Execute(new(HostCommandKind.AssignWork));
            Check(!invalid.Succeeded && invalid.Message.Contains("fresh current desktop"));
        });
        Test("unsupported mismatched invalid settings and stale observations disable setup accurately", () =>
        {
            using var f = new Fixture(); using (var unsupported = f.App(false))
            { Check(!unsupported.Snapshot().CanConsent && !unsupported.Snapshot().CanAssign); }
            f.Enable(); using (var mismatch = f.App(build: "26200.9711"))
            { Check(mismatch.Snapshot().CanConsent && !mismatch.Snapshot().CanAssign && mismatch.Snapshot().ConsentNeedsReview); }
            using (var app = f.App())
            {
                f.Advance(app, 0); Check(app.Snapshot().PersonalAssignment == AssignmentStatus.Valid);
                f.Clock.Now = f.Clock.Now with { Milliseconds = 2_001 };
                Check(app.Snapshot().PersonalAssignment == AssignmentStatus.Unavailable && !app.Snapshot().CanAssign);
            }
            File.WriteAllText(Path.Combine(f.PathName, "windows-settings.json"), "{broken");
            using var invalid = f.App(); Check(!invalid.Snapshot().CanConsent && !invalid.Snapshot().CanAssign);
            Check(invalid.Snapshot().PersonalAssignment == AssignmentStatus.Unavailable && invalid.Snapshot().WorkAssignment == AssignmentStatus.Unavailable);
        });
        Test("provider retry construction failure is an explicit failed outcome and retains cooldown", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            f.Advance(app, 0); f.Source!.FailRead = true; f.Advance(app, 1_000); f.FailCreate = true;
            var failed = app.Execute(new(HostCommandKind.RetryMonitoring));
            Check(!failed.Succeeded && failed.Message.Contains("could not be restarted"));
            Check(!app.Snapshot().Available && !app.Snapshot().CanAssign && app.Snapshot().RetryInMs == 10_000);
            var attempts = f.ProviderCreates;
            Check(!app.Execute(new(HostCommandKind.RetryMonitoring)).Succeeded && f.ProviderCreates == attempts);
            f.FailCreate = false; f.Advance(app, 11_000);
            var retry = app.Execute(new(HostCommandKind.RetryMonitoring));
            Check(retry.Succeeded && retry.Message.Contains("Waiting for fresh evidence"));
        });
        Test("close preference save failures preserve settings and successful choice changes only close behavior", () =>
        {
            using var f = new Fixture(); f.Enable();
            var original = f.Settings.Load() with { Theme = AppTheme.Dark };
            f.Settings.Save(original); using var app = f.App();
            var bytes = File.ReadAllBytes(Path.Combine(f.PathName, "windows-settings.json"));
            f.Settings.BeforeReplace = () => throw new IOException("Injected save failure");
            foreach (var choice in new[] { true, false })
            {
                var result = app.Execute(new(HostCommandKind.ChangeCloseBehavior) { CloseToTray = choice });
                Check(!result.Succeeded && result.Message.Contains("could not be saved"));
                Check(app.Snapshot().Settings == original && f.Settings.Load() == original);
                Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(f.PathName, "windows-settings.json"))));
            }
            f.Settings.BeforeReplace = null;
            Check(app.Execute(new(HostCommandKind.ChangeCloseBehavior) { CloseToTray = true }).Succeeded);
            Check(app.Snapshot().Settings == original with { CloseToTray = true });
        });
        Test("integrated exact-build policy reaches the host without bypassing saved setup gates", () =>
        {
            foreach (var build in new[] { "26200.9457", "26200.9711", "26200.9712" })
            {
                using var f = new Fixture(); f.Enable(build: build);
                using var app = f.App(WindowsObservation.DesktopObservationPolicy.IsSupported(build, "X64"), build);
                for (var i = 0; i <= 5_000; i += 1_000) f.Advance(app, i);
                var approved = build != "26200.9712";
                Check(f.ProviderCreates == (approved ? 1 : 0));
                Check(approved ? app.Snapshot().State!.UsageMs > 0 : app.Snapshot().Status == "Unavailable");
            }
            using var stale = new Fixture(); stale.Enable(build: "26200.9457");
            using var updated = stale.App(WindowsObservation.DesktopObservationPolicy.IsSupported("26200.9711", "X64"), "26200.9711");
            for (var i = 0; i <= 5_000; i += 1_000) stale.Advance(updated, i);
            Check(stale.ProviderCreates == 0); Check(updated.Snapshot().State!.UsageMs == 0);
            Check(updated.Snapshot().Detail.Contains("Explicitly review setup again", StringComparison.Ordinal));
        });
        Test("first run retains CertifiedOnly and creates no native provider before consent", () =>
        {
            using var f = new Fixture(); using var app = f.App();
            Check(f.ProviderCreates == 0); Check(app.Snapshot().Status == "Setup required");
            Check(app.Snapshot().Configuration!.Accounting == AccountingMode.CertifiedOnly);
            Check(app.Execute(new(HostCommandKind.Start)).Message.Length > 0);
        });
        Test("first setup requires explicit consent assignments and Start before charging", () =>
        {
            using var f = new Fixture(); using var app = f.App();
            Check(app.Execute(new(HostCommandKind.Consent)).Message.Length > 0); Check(f.ProviderCreates == 0);
            Check(app.Execute(new(HostCommandKind.Consent) { Confirmed = true }).Succeeded);
            f.Advance(app, 0); app.Execute(new(HostCommandKind.AssignPersonal));
            f.Source!.Current = f.Work; app.Execute(new(HostCommandKind.AssignWork));
            for (var i = 1_000; i <= 3_000; i += 1_000) f.Advance(app, i);
            Check(app.Snapshot().State!.UsageMs == 0 && !app.Snapshot().Settings.MonitoringEnabled);
            Check(app.Execute(new(HostCommandKind.Start)).Succeeded);
            for (var i = 4_000; i <= 8_000; i += 1_000) f.Advance(app, i);
            Check(app.Snapshot().Configuration!.Accounting == AccountingMode.BestEffort);
            Check(app.Snapshot().State!.UsageMs > 0);
        });
        Test("holdback finalizes independently at its deadline without early credit", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            f.Advance(app, 0); f.Advance(app, 1_000); f.Advance(app, 1_999);
            Check(app.Snapshot().State!.UsageMs == 0);
            f.Advance(app, 2_000); Check(app.Snapshot().State!.UsageMs == 1_000);
        });
        Test("event sampling coalesces and never runs within the 100 ms minimum", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App(); f.Advance(app, 0);
            var reads = f.Source!.Reads;
            f.Clock.Now = f.Clock.Now with { Milliseconds = 10 }; f.Source.Emit(WindowsObservation.SignalKind.Foreground); app.Step();
            f.Advance(app, 60); Check(f.Source.Reads == reads);
            f.Advance(app, 100); Check(f.Source.Reads == reads + 1);
        });
        Test("saved enabled monitoring resumes with fresh baselines and no downtime", () =>
        {
            using var f = new Fixture(); f.Enable();
            long usage;
            using (var app = f.App())
            {
                for (var i = 0; i <= 5_000; i += 1_000) f.Advance(app, i);
                usage = app.Snapshot().State!.UsageMs; Check(usage == 4_000);
            }
            f.Clock.Now = new(Guid.NewGuid(), 0);
            using var reopened = f.App(); Check(reopened.Snapshot().State!.UsageMs == usage);
            f.Advance(reopened, 0); Check(reopened.Snapshot().State!.UsageMs == usage);
            f.Advance(reopened, 1_000); Check(reopened.Snapshot().State!.UsageMs == usage);
        });
        Test("pause is durable and resumed accounting requires fresh endpoints", () =>
        {
            using var f = new Fixture(); f.Enable();
            using (var app = f.App())
            { for (var i = 0; i <= 3_000; i += 1_000) f.Advance(app, i); Check(app.Execute(new(HostCommandKind.Pause)).Succeeded); }
            f.Clock.Now = new(Guid.NewGuid(), 0);
            using var restored = f.App(); Check(restored.Snapshot().State!.Paused);
            var usage = restored.Snapshot().State!.UsageMs;
            for (var i = 0; i <= 5_000; i += 1_000) f.Advance(restored, i);
            Check(restored.Snapshot().State!.UsageMs == usage);
            Check(restored.Execute(new(HostCommandKind.Resume)).Succeeded);
            f.Advance(restored, 6_000); Check(restored.Snapshot().State!.UsageMs == usage);
        });
        Test("missing or incompatible settings never enable accounting from a BestEffort checkpoint alone", () =>
        {
            using var f = new Fixture(); f.Enable(); File.Delete(Path.Combine(f.PathName, "windows-settings.json"));
            using var app = f.App(); Check(f.ProviderCreates == 0); Check(app.Snapshot().State!.UsageMs == 0);
        });
        Test("unsupported build does not construct providers or account", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App(false);
            for (var i = 0; i <= 5_000; i += 1_000) f.Advance(app, i);
            Check(f.ProviderCreates == 0); Check(app.Snapshot().Status == "Unavailable"); Check(app.Snapshot().State!.UsageMs == 0);
        });
        Test("deleted GUIDs fail closed and desktop reorder does not change roles", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            f.Source!.Topology = [f.Work, f.Personal]; f.Advance(app, 0);
            Check(app.Snapshot().Context == DesktopScope.Personal);
            f.Source.Topology = [f.Personal, Guid.NewGuid()];
            for (var i = 1_000; i <= 4_000; i += 1_000) f.Advance(app, i);
            Check(!app.Snapshot().BindingsValid); Check(app.Snapshot().State!.UsageMs == 0);
        });
        Test("assignment captures now rather than previous polling result and stops monitoring", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App(); f.Advance(app, 0);
            f.Source!.Current = f.Work;
            Check(app.Execute(new(HostCommandKind.AssignPersonal)).Message.Length > 0); // duplicate roles rejected
            var third = Guid.NewGuid(); f.Source.Topology = [f.Personal, f.Work, third]; f.Source.Current = third;
            app.Execute(new(HostCommandKind.AssignPersonal));
            Check(app.Snapshot().Settings.Personal == third); Check(!app.Snapshot().Settings.MonitoringEnabled);
        });
        foreach (var fault in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test("exact pending retry after " + fault, () =>
            {
                using var f = new Fixture(); f.Enable(); using var app = f.App();
                f.Fault = point => { if (point == fault) throw new IOException(); };
                app.Execute(new(HostCommandKind.ChangeDurations) { AllowanceMs = 123_000, RequiredBreakMs = 45_000, IdleThresholdMs = 6_000 });
                Check(app.Snapshot().Persistence == PersistenceStatus.PendingCommit); Check(app.Snapshot().State is null);
                Check(app.Execute(new(HostCommandKind.Pause)).Message.Length > 0);
                f.Fault = null; Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
                Check(app.Snapshot().Configuration!.AllowanceMs == 123_000); Check(app.Snapshot().State!.EffectSequence == 0);
                Check(app.Execute(new(HostCommandKind.RetrySave)).Message.Length > 0);
            });
        Test("provider failure requires explicit retry and cooldown; totals remain saved", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            for (var i = 0; i <= 3_000; i += 1_000) f.Advance(app, i);
            var usage = app.Snapshot().State!.UsageMs; var source = f.Source!;
            source.FailRead = true; f.Advance(app, 4_000);
            Check(app.Snapshot().Status == "Unavailable"); f.Advance(app, 8_000); Check(f.ProviderCreates == 1);
            Check(app.Execute(new(HostCommandKind.RetryMonitoring)).Succeeded); Check(source.Disposed);
            Check(app.Execute(new(HostCommandKind.RetryMonitoring)).Message.Length > 0);
            Check(app.Snapshot().State!.UsageMs == usage);
        });
        Test("one desktop helper replacement per shell generation", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            f.Advance(app, 0); f.Source!.ShellGeneration = 2;
            for (var i = 1_000; i <= 15_000; i += 1_000) f.Advance(app, i);
            Check(f.Source.Replacements == 1);
        });
        Test("slow delivery gap earns no retroactive usage", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            for (var i = 0; i <= 3_000; i += 1_000) f.Advance(app, i);
            var usage = app.Snapshot().State!.UsageMs;
            f.Advance(app, 30_000); Check(app.Snapshot().State!.UsageMs == usage);
        });
        Test("corrupt settings require deliberate preservation and do not reset Core", () =>
        {
            using var f = new Fixture(); f.Enable(); var path = Path.Combine(f.PathName, "windows-settings.json");
            File.WriteAllText(path, "{broken"); using var app = f.App();
            Check(app.Snapshot().SettingsStatus == SettingsStatus.Invalid); Check(f.ProviderCreates == 0);
            Check(app.Execute(new(HostCommandKind.RepairSettings)).Message.Length > 0);
            Check(app.Execute(new(HostCommandKind.RepairSettings) { Confirmed = true }).Succeeded);
            Check(Directory.GetFiles(f.PathName, "*.preserved-*").Length == 1);
            Check(app.Snapshot().Configuration!.Accounting == AccountingMode.BestEffort);
        });
        Test("strict settings reject duplicates, unknown fields and future versions", () =>
        {
            using var f = new Fixture();
            var valid = JsonSerializer.Serialize(new WindowsSettings());
            foreach (var payload in new[] { valid[..^1] + ",\"Version\":1}", valid[..^1] + ",\"extra\":1}", "{\"Version\":2}", "{\"Version\":\"bad\"}", "{\"Version\":99999999999999999999}", "{}" })
            {
                File.WriteAllText(Path.Combine(f.PathName, "windows-settings.json"), payload); f.Settings.Load();
                Check(f.Settings.Status is SettingsStatus.Invalid or SettingsStatus.Incompatible);
            }
        });
        Test("interrupted settings replacement leaves the previous file intact", () =>
        {
            using var f = new Fixture(); f.Enable(); var before = File.ReadAllBytes(Path.Combine(f.PathName, "windows-settings.json"));
            f.Settings.BeforeReplace = () => throw new IOException();
            try { f.Settings.Save(new() { Theme = AppTheme.Dark }); } catch (IOException) { }
            Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(f.PathName, "windows-settings.json"))));
        });
        Test("recovery is forbidden for incompatible and clock-blocked storage", () =>
        {
            using var f = new Fixture(); f.Enable(); f.Clock.UtcNow = f.Clock.UtcNow.AddDays(-1);
            using var app = f.App(); Check(app.Snapshot().Persistence == PersistenceStatus.Blocked);
            Check(app.Execute(new(HostCommandKind.Recover) { Confirmed = true }).Message.Length > 0);
            Check(app.Snapshot().Persistence == PersistenceStatus.Blocked);
        });
        Test("shutdown disposes providers and releases the checkpoint lease", () =>
        {
            using var f = new Fixture(); f.Enable(); var app = f.App(); var source = f.Source!;
            app.Dispose(); Check(source.Disposed); using var session = f.Session(); Check(session.Status == PersistenceStatus.Ready);
        });
        Test("controller serializes commands and publishes only acknowledged configuration", () =>
        {
            using var f = new Fixture();
            var controller = new MonitoringController(() => f.App());
            var changes = Enumerable.Range(1, 8).Select(i => controller.ExecuteAsync(new(HostCommandKind.ChangeDurations)
            { AllowanceMs = i * 60_000, RequiredBreakMs = 30_000, IdleThresholdMs = 6_000 })).ToArray();
            Check(Task.WaitAll(changes, 10_000)); Check(changes.All(x => x.Result.Succeeded));
            Check(controller.Latest!.Configuration!.AllowanceMs == 8 * 60_000);
            Check(controller.StopAsync().Wait(5_000));
            Check(controller.ExecuteAsync(new(HostCommandKind.Pause)).Result.Message.Length > 0);
            using var restored = f.Session(); Check(restored.Configuration.AllowanceMs == 8 * 60_000);
        });
        Test("bounded command queue and shutdown resolve every submitted command", () =>
        {
            using var f = new Fixture(); using var initialize = new ManualResetEventSlim();
            var controller = new MonitoringController(() => { initialize.Wait(); return f.App(); });
            var commands = Enumerable.Range(0, 64).Select(_ => controller.ExecuteAsync(new(HostCommandKind.Pause))).ToArray();
            Check(commands.Count(x => x.IsCompleted) == 48);
            var stopped = controller.StopAsync(); initialize.Set();
            Check(stopped.Wait(5_000)); Check(Task.WaitAll(commands, 5_000)); Check(commands.All(x => x.Result.Message.Length > 0));
        });
        Test("recovery preserves corrupt originals and requires another explicit Start", () =>
        {
            using var f = new Fixture(); f.Enable();
            File.WriteAllText(Path.Combine(f.PathName, "checkpoint.db"), "corrupt-test-database");
            using var app = f.App(); Check(app.Snapshot().Persistence == PersistenceStatus.RecoveryRequired);
            Check(app.Execute(new(HostCommandKind.Recover)).Message.Length > 0);
            Check(app.Execute(new(HostCommandKind.Recover) { Confirmed = true }).Succeeded);
            Check(app.Snapshot().Persistence == PersistenceStatus.Ready);
            Check(!app.Snapshot().Settings.MonitoringEnabled);
            var archives = Directory.GetDirectories(f.PathName, "recovery-*"); Check(archives.Length == 1);
            Check(File.ReadAllText(Path.Combine(archives[0], "checkpoint.db")) == "corrupt-test-database");
        });
        Test("incompatible Windows settings remain untouched and cannot be repaired", () =>
        {
            using var f = new Fixture(); f.Enable(); var path = Path.Combine(f.PathName, "windows-settings.json");
            File.WriteAllText(path, "{\"Version\":999}"); using var app = f.App();
            Check(app.Snapshot().SettingsStatus == SettingsStatus.Incompatible);
            Check(app.Execute(new(HostCommandKind.RepairSettings) { Confirmed = true }).Message.Length > 0);
            Check(File.ReadAllText(path) == "{\"Version\":999}"); Check(f.ProviderCreates == 0);
        });
        MonitoringReliabilityRegressions.Run();
        Pr8Regressions.Run();
        SettingsConflictRegressions.Run();
        PresentationEpochRegressions.Run();
        InteractiveHostRegressions.Run();
        SwitchRegressions.Run();
        SwitchPolicyRegressions.Run();
        SwitchWorkerGuardRegressions.Run();
        FamilyCompatibilityRegressions.Run();
        Console.WriteLine($"Host: {passed} passed, {failed} failed, {assertions} assertions (synthetic providers, real local SQLite).");
        return failed == 0 ? 0 : 1;
    }
}
