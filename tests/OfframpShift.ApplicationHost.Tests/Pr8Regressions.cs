using System.Text.Json;
using System.Text.Json.Nodes;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;
using static Program;

internal static class Pr8Regressions
{
    private static MonitoringApplication ReminderApp(Fixture f, bool enabled = true)
    {
        f.Enable();
        f.Settings.Save(f.Settings.Load() with { ReminderStyle = enabled ? ReminderStyle.WindowsNotification : ReminderStyle.Off });
        using (var session = f.Session())
            session.ChangeConfiguration(session.Configuration with
            { AllowanceMs = 2_000, GraceMs = 1_000, RepeatNudgeMs = 3_000, RequiredBreakMs = 10_000, IdleThresholdMs = 6_000 });
        var app = f.App();
        for (var ms = 0; ms <= 3_000; ms += 1_000)
        {
            f.Advance(app, ms);
            if (ms < 3_000) Check(!app.Reminders.TryTake(Environment.TickCount64));
        }
        Check(app.Snapshot().State!.Intervention == InterventionState.Nudging);
        return app;
    }

    internal static void Run()
    {
        foreach (var style in new[] { ReminderStyle.InteractiveWindow, ReminderStyle.WindowsNotification })
            Test($"idle Personal advances committed cadence without presentation or replay: {style}", () =>
            {
                using var f = new Fixture(); f.Enable();
                f.Settings.Save(f.Settings.Load() with { ReminderStyle = style });
                using (var session = f.Session())
                    session.ChangeConfiguration(session.Configuration with
                    { AllowanceMs = 2_000, RequiredBreakMs = 10_000, IdleThresholdMs = 6_000 });
                using var app = f.App();
                for (var ms = 0; ms <= 125_000; ms += 1_000)
                {
                    // The first observation is already idle. No pre-baseline credit.
                    f.Source!.IdleAgeMs = 60_000 + ms;
                    f.Advance(app, ms);
                    Check(!app.Reminders.TryTake(Environment.TickCount64));
                    Check(app.Snapshot().State!.BreakProgressMs == 0);
                }
                var before = app.Snapshot().State!;
                Check(before.UsageMs == 124_000 && before.Exhausted && !before.BreakCompleted);
                Check(before.EffectSequence == 4 && before.ReminderRemainingMs == 58_000);
                f.Source!.IdleAgeMs = 0;
                for (var ms = 126_000; ms <= 130_000; ms += 1_000)
                {
                    f.Advance(app, ms);
                    Check(!app.Reminders.TryTake(Environment.TickCount64));
                }
                Check(app.Snapshot().State!.UsageMs == 129_000);
                Check(app.Snapshot().State!.EffectSequence == before.EffectSequence);
            });
        Test("strict v1 migration preserves every preference and writes v5 only on explicit save", () =>
        {
            using var f = new Fixture(); f.Enable();
            var expected = f.Settings.Load() with { Theme = AppTheme.Dark, CloseToTray = true };
            var legacy = JsonSerializer.SerializeToNode(expected)!;
            legacy["Version"] = 1; legacy.AsObject().Remove("ReminderStyle");
            legacy.AsObject().Remove("SwitchingConsent"); legacy.AsObject().Remove("SwitchingConsentBuild"); legacy.AsObject().Remove("SwitchingConsentFamily");
            var path = Path.Combine(f.PathName, "windows-settings.json");
            File.WriteAllText(path, legacy.ToJsonString()); var bytes = File.ReadAllBytes(path);
            Check(f.Settings.Load() == expected && f.Settings.Status == SettingsStatus.Ready);
            Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
            using var app = f.App();
            f.Settings.BeforeReplace = () => throw new IOException();
            Check(!app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = ReminderStyle.WindowsNotification }).Succeeded);
            Check(app.Snapshot().Settings == expected && bytes.SequenceEqual(File.ReadAllBytes(path)));
            f.Settings.BeforeReplace = null;
            Check(app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = ReminderStyle.WindowsNotification }).Succeeded);
            Check(f.Settings.Load() == expected with { ReminderStyle = ReminderStyle.WindowsNotification });
            Check(JsonNode.Parse(File.ReadAllText(path))!["Version"]!.GetValue<int>() == 5);
        });
        Test("both settings versions reject missing duplicate unknown and invalid fields; future versions stay protected", () =>
        {
            using var f = new Fixture(); var path = Path.Combine(f.PathName, "windows-settings.json");
            foreach (var version in new[] { 1, 2, 3, 4, 5 })
            {
                var node = JsonSerializer.SerializeToNode(new WindowsSettings())!.AsObject(); node["Version"] = version;
                if (version < 5) node.Remove("SwitchingConsentFamily");
                if (version < 4) { node.Remove("SwitchingConsent"); node.Remove("SwitchingConsentBuild"); node.Remove("SwitchingConsentFamily"); }
                if (version < 3) node.Remove("ReminderStyle");
                if (version == 2) node["BreakRemindersEnabled"] = false;
                var valid = node.ToJsonString();
                foreach (var name in node.Select(x => x.Key).ToArray())
                {
                    var missing = JsonNode.Parse(valid)!.AsObject(); missing.Remove(name);
                    File.WriteAllText(path, missing.ToJsonString()); f.Settings.Load(); Check(f.Settings.Status == SettingsStatus.Invalid);
                }
                foreach (var bad in new[] { valid[..^1] + ",\"Consent\":true}", valid[..^1] + ",\"extra\":1}",
                    valid.Replace("\"Consent\":false", "\"Consent\":null", StringComparison.Ordinal),
                    version != 2 ? valid[..^1] + ",\"BreakRemindersEnabled\":true}" : valid.Replace("\"BreakRemindersEnabled\":false", "\"BreakRemindersEnabled\":null", StringComparison.Ordinal) })
                {
                    File.WriteAllText(path, bad); f.Settings.Load(); Check(f.Settings.Status == SettingsStatus.Invalid);
                }
            }
            File.WriteAllText(path, "{\"Version\":6}"); f.Settings.Load(); Check(f.Settings.Status == SettingsStatus.Incompatible);
            try { f.Settings.Save(new()); Check(false); } catch (InvalidOperationException) { }
            Check(File.ReadAllText(path) == "{\"Version\":6}");
        });
        Test("reminders default off and toggling never changes accounting or unfinished break", () =>
        {
            using var f = new Fixture(); Check(f.Settings.Load().ReminderStyle == ReminderStyle.Off);
            using var app = ReminderApp(f, false);
            Check(!app.Reminders.TryTake(Environment.TickCount64));
            var before = app.Snapshot();
            Check(app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = ReminderStyle.WindowsNotification }).Succeeded);
            Check(app.Snapshot().State == before.State && app.Snapshot().Configuration == before.Configuration);
            Check(!app.Reminders.TryTake(Environment.TickCount64));
            f.Source!.Current = f.Work;
            for (var ms = 5_000; ms <= 7_000; ms += 1_000) f.Advance(app, ms);
            before = app.Snapshot(); Check(before.State!.BreakProgressMs > 0);
            Check(app.Execute(new(HostCommandKind.ChangeBreakReminders)).Succeeded);
            Check(app.Snapshot().State == before.State && app.Snapshot().Configuration == before.Configuration);
            Check(app.Snapshot().Settings == before.Settings with { ReminderStyle = ReminderStyle.Off });
        });
        Test("first and repeated committed nudges are consumed once using Core cadence", () =>
        {
            using var f = new Fixture(); using var app = ReminderApp(f);
            Check(app.Reminders.TryTake(Environment.TickCount64));
            for (var i = 0; i < 5; i++) { app.Snapshot(); app.Step(); Check(!app.Reminders.TryTake(Environment.TickCount64)); }
            for (var ms = 4_000; ms <= 63_000; ms += 1_000)
            {
                f.Advance(app, ms);
                Check(app.Reminders.TryTake(Environment.TickCount64) == (ms == 63_000));
            }
        });
        Test("mailbox expires without refresh renewal, deduplicates, invalidates and has one consumer", () =>
        {
            var mailbox = new BreakReminderMailbox();
            mailbox.Offer(1, 100); mailbox.Update(true, 100, 2_000);
            Check(!mailbox.TryTake(2_101));
            mailbox.Offer(1, 2_101); mailbox.Update(true, 2_101, 2_000); Check(!mailbox.TryTake(2_101));
            mailbox.Offer(2, 3_000); mailbox.Update(true, 3_000, 5); Check(!mailbox.TryTake(3_006));
            mailbox.Offer(3, 4_000); mailbox.Update(true, 4_000, 2_000);
            mailbox.Update(true, 6_001, 2_000); Check(!mailbox.TryTake(6_001));
            mailbox.Offer(4, 7_000); mailbox.Update(true, 7_000, 2_000); mailbox.Invalidate(); Check(!mailbox.TryTake(7_000));
            mailbox.Offer(5, 8_000); mailbox.Update(true, 8_000, 2_000);
            var consumed = 0; Parallel.For(0, 32, _ => { if (mailbox.TryTake(8_000)) Interlocked.Increment(ref consumed); });
            Check(consumed == 1);
            mailbox.Offer(6, 9_000); mailbox.Update(false, 9_000, 2_000); Check(!mailbox.TryTake(9_000));
            mailbox.Offer(7, 10_000); mailbox.Update(true, 10_000, 2_000); Check(!mailbox.TryTake(9_999));
            mailbox.ResetSession(); Check(!mailbox.TryTake(10_000));
            mailbox.Offer(1, 11_000); mailbox.Update(true, 11_000, 2_000); Check(mailbox.TryTake(11_000));
        });
        foreach (var condition in new[] { "Paused", "Work", "Other", "Idle", "Locked", "Unavailable", "Input", "Missing", "Identity", "Shell", "Dropped", "Stale", "Settings", "Reset", "Retry", "Signal" })
            Test("pending reminder is discarded with no replay: " + condition, () =>
            {
                using var f = new Fixture(); using var app = ReminderApp(f);
                switch (condition)
                {
                    case "Paused": app.Execute(new(HostCommandKind.Pause)); break;
                    case "Work": f.Source!.Current = f.Work; break;
                    case "Other": f.Source!.Current = Guid.NewGuid(); f.Source.Topology = [f.Personal, f.Work, f.Source.Current.Value]; break;
                    case "Idle": f.Source!.IdleAgeMs = 6_000; break;
                    case "Locked": f.Source!.Session = SessionState.Locked; break;
                    case "Unavailable": f.Source!.Hooks = Capability.Unavailable; break;
                    case "Input": f.Source!.Input = Capability.Unavailable; break;
                    case "Missing": f.Source!.Topology = [f.Personal]; break;
                    case "Identity": f.Source!.Application = new("different-app", true); break;
                    case "Shell": f.Source!.ShellGeneration++; break;
                    case "Dropped": f.Source!.Dropped++; break;
                    case "Stale": f.Clock.Now = f.Clock.Now with { Milliseconds = 10_000 }; break;
                    case "Settings": app.Execute(new(HostCommandKind.ChangeAppearance) { Theme = AppTheme.Dark }); break;
                    case "Reset": Check(app.Execute(new(HostCommandKind.ResetUsage) { Confirmed = true }).Succeeded); break;
                    case "Retry": Check(app.Execute(new(HostCommandKind.RetryMonitoring)).Succeeded); break;
                    case "Signal": f.Source!.Emit(SignalKind.Foreground); app.Step(); break;
                }
                if (condition is "Work" or "Other" or "Idle" or "Locked" or "Unavailable" or "Input" or "Missing" or "Identity" or "Shell" or "Dropped" or "Stale")
                    f.Advance(app, condition == "Stale" ? 10_000 : 5_000);
                Check(!app.Reminders.TryTake(Environment.TickCount64));
                app.Step(); Check(!app.Reminders.TryTake(Environment.TickCount64));
            });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test("failed nudge commit and save retry never deliver delayed effects: " + stage, () =>
            {
                using var f = new Fixture(); using var app = ReminderApp(f);
                Check(app.Reminders.TryTake(Environment.TickCount64));
                for (var ms = 4_000; ms <= 62_000; ms += 1_000) f.Advance(app, ms);
                f.Fault = point => { if (point == stage) throw new IOException(); };
                f.Advance(app, 63_000);
                Check(app.Snapshot().Persistence == PersistenceStatus.PendingCommit && !app.Reminders.TryTake(Environment.TickCount64));
                f.Fault = null; Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
                Check(!app.Reminders.TryTake(Environment.TickCount64));
                f.Advance(app, 64_000); f.Advance(app, 65_000); Check(!app.Reminders.TryTake(Environment.TickCount64));
            });
        Test("slow commit and contradictory signals after commit suppress new reminders", () =>
        {
            foreach (var slow in new[] { false, true })
            {
                using var f = new Fixture(); using var app = ReminderApp(f);
                Check(app.Reminders.TryTake(Environment.TickCount64));
                for (var ms = 4_000; ms <= 62_000; ms += 1_000) f.Advance(app, ms);
                f.Fault = stage =>
                {
                    if (stage != StorageFault.AfterCommit) return;
                    if (slow) f.Clock.Now = f.Clock.Now with { Milliseconds = 66_000 };
                    else f.Source!.Emit(SignalKind.InputDesktopChanged);
                };
                f.Advance(app, 63_000); Check(!app.Reminders.TryTake(Environment.TickCount64));
            }
        });
        Test("restart restores cadence but never pending reminder delivery", () =>
        {
            using var f = new Fixture();
            using (var app = ReminderApp(f)) { Check(app.Snapshot().State!.Exhausted); }
            f.Clock.Now = new(Guid.NewGuid(), 0);
            using var restored = f.App(); Check(!restored.Reminders.TryTake(Environment.TickCount64));
            f.Advance(restored, 0); f.Advance(restored, 1_000); Check(!restored.Reminders.TryTake(Environment.TickCount64));
        });
        Test("confirmed reset preserves settings config Pause and requires fresh endpoints", () =>
        {
            foreach (var paused in new[] { false, true })
            {
                using var f = new Fixture(); using var app = ReminderApp(f);
                if (paused) Check(app.Execute(new(HostCommandKind.Pause)).Succeeded);
                var before = app.Snapshot();
                Check(!app.Execute(new(HostCommandKind.ResetUsage)).Succeeded);
                Check(app.Snapshot().State == before.State);
                Check(app.Execute(new(HostCommandKind.ResetUsage) { Confirmed = true }).Succeeded);
                var after = app.Snapshot();
                Check(after.State!.UsageMs == 0 && after.State.BreakProgressMs == 0 && !after.State.BreakCompleted && !after.State.Exhausted);
                Check(after.State.Paused == paused && after.State.EffectSequence == before.State!.EffectSequence);
                Check(after.Settings == before.Settings && after.Configuration == before.Configuration);
                Check(!app.Reminders.TryTake(Environment.TickCount64));
                f.Advance(app, 5_000); Check(app.Snapshot().State!.UsageMs == 0);
            }
        });
        Test("failed reset is not reported saved and explicit retry preserves exact reset", () =>
        {
            using var f = new Fixture(); using var app = ReminderApp(f);
            var settings = app.Snapshot().Settings;
            f.Fault = stage => { if (stage == StorageFault.BeforeUpdate) throw new IOException(); };
            Check(!app.Execute(new(HostCommandKind.ResetUsage) { Confirmed = true }).Succeeded);
            Check(app.Snapshot().Persistence == PersistenceStatus.PendingCommit && app.Snapshot().State is null);
            Check(!app.Reminders.TryTake(Environment.TickCount64));
            Check(!app.Execute(new(HostCommandKind.ResetUsage) { Confirmed = true }).Succeeded);
            f.Fault = null; Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
            Check(app.Snapshot().State!.UsageMs == 0 && app.Snapshot().Settings == settings);
            Check(!app.Reminders.TryTake(Environment.TickCount64));
        });
        Test("Ready reset remains available with unsupported or failed providers", () =>
        {
            using var f = new Fixture(); f.Enable();
            using var app = f.App(false);
            Check(app.Execute(new(HostCommandKind.ResetUsage) { Confirmed = true }).Succeeded && f.ProviderCreates == 0);
        });
    }
}
