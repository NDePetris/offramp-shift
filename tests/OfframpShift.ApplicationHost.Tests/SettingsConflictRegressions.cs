using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;
using static Program;

internal static class SettingsConflictRegressions
{
    internal static void Run()
    {
        foreach (var kind in new[] { HostCommandKind.ChangeDurations, HostCommandKind.ChangeInteractiveRepeat,
            HostCommandKind.ChangeAppearance, HostCommandKind.ChangeBreakReminders, HostCommandKind.ChangeSwitchingConsent })
            Test("settings expected-value conflict is structured and leaves committed state unchanged: " + kind, () =>
            {
                using var f = new Fixture(); f.Enable(); using var app = f.App();
                var before = app.Snapshot();
                var expected = ExpectedSettingsValues.From(before.Settings, before.Configuration);
                expected = kind switch
                {
                    HostCommandKind.ChangeDurations => expected with { AllowanceMs = expected.AllowanceMs + 1 },
                    HostCommandKind.ChangeInteractiveRepeat => expected with { RepeatMs = expected.RepeatMs + 1 },
                    HostCommandKind.ChangeAppearance => expected with { Theme = AppTheme.Dark },
                    HostCommandKind.ChangeBreakReminders => expected with { ReminderStyle = ReminderStyle.InteractiveWindow },
                    _ => expected with { SwitchingConsent = true }
                };
                var bytes = File.ReadAllBytes(Path.Combine(f.PathName, "windows-settings.json"));
                var result = app.Execute(new(kind) { Expected = expected });
                var after = app.Snapshot();
                Check(!result.Succeeded && result.ConfigurationConflict);
                Check(before.Configuration == after.Configuration && before.State == after.State && before.Settings == after.Settings);
                Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(f.PathName, "windows-settings.json"))));
            });
        Test("unrelated configuration and preference changes do not conflict with command-owned fields", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            var seed = app.Snapshot(); var expected = ExpectedSettingsValues.From(seed.Settings, seed.Configuration);
            Check(app.Execute(new(HostCommandKind.ChangeInteractiveRepeat) { DurationMs = 120_000 }).Succeeded);
            Check(app.Execute(new(HostCommandKind.ChangeAppearance) { Theme = AppTheme.Dark, CloseToTray = true }).Succeeded);
            Check(app.Execute(new(HostCommandKind.ChangeDurations) { Expected = expected, AllowanceMs = 600_000, RequiredBreakMs = 60_000, IdleThresholdMs = 1_000 }).Succeeded);
            Check(app.Execute(new(HostCommandKind.ChangeBreakReminders) { Expected = expected, ReminderStyle = ReminderStyle.WindowsNotification }).Succeeded);
            var current = app.Snapshot(); Check(current.Configuration!.InteractiveRepeatNudgeMs == 120_000 && current.Settings.Theme == AppTheme.Dark);
        });
        Test("unavailable accounting rejects before conflict and exact Retry save keeps its checkpoint", () =>
        {
            using var f = new Fixture(); f.Enable(); using var app = f.App();
            var seed = app.Snapshot(); var expected = ExpectedSettingsValues.From(seed.Settings, seed.Configuration);
            f.Fault = fault => { if (fault == StorageFault.AfterUpdate) throw new IOException("Injected commit failure"); };
            var command = new HostCommand(HostCommandKind.ChangeDurations) { Expected = expected, AllowanceMs = 600_000, RequiredBreakMs = 60_000, IdleThresholdMs = 1_000 };
            Check(!app.Execute(command).Succeeded && app.Snapshot().Persistence == PersistenceStatus.PendingCommit);
            var denied = app.Execute(command); Check(!denied.Succeeded && !denied.ConfigurationConflict);
            f.Fault = null; Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
            var saved = app.Snapshot(); Check(saved.Configuration!.AllowanceMs == 600_000 && saved.Configuration.RequiredBreakMs == 60_000);
            Check(!app.Execute(command).Succeeded && app.Snapshot().Configuration == saved.Configuration);
        });
        Test("conflicting save still revokes switch grant and active presentation without mutating accounting", () =>
        {
            using var f = new Fixture(); using var app = SwitchRegressions.Active(f);
            var granted = app.Execute(new(HostCommandKind.AuthorizeSwitch) { NudgeWindow = 2 });
            Check(granted.Succeeded && granted.Switching is not null);
            var before = app.Snapshot(); Check(before.PresentationId != Guid.Empty);
            var expected = ExpectedSettingsValues.From(before.Settings, before.Configuration) with { Theme = AppTheme.Dark };
            var result = app.Execute(new(HostCommandKind.ChangeAppearance) { Expected = expected });
            Check(result.ConfigurationConflict && !result.Succeeded);
            Check(granted.Switching!.Consume(Environment.TickCount64) is null);
            Check(app.Snapshot().PresentationId == Guid.Empty && app.Snapshot().State == before.State);
            Check(!app.Reminders.TryTake(Environment.TickCount64, out _));
        });
        Test("conflicting save invalidates an unconsumed reminder mailbox event", () =>
        {
            using var f = new Fixture(); f.Enable();
            f.Settings.Save(f.Settings.Load() with { ReminderStyle = ReminderStyle.WindowsNotification });
            using (var session = f.Session()) session.ChangeConfiguration(session.Configuration with { AllowanceMs = 2_000 });
            using var app = f.App(); for (var ms = 0; ms <= 3_000; ms += 1_000) f.Advance(app, ms);
            var before = app.Snapshot(); var expected = ExpectedSettingsValues.From(before.Settings, before.Configuration) with { Theme = AppTheme.Dark };
            Check(app.Execute(new(HostCommandKind.ChangeAppearance) { Expected = expected }).ConfigurationConflict);
            Check(!app.Reminders.TryTake(Environment.TickCount64, out _));
            Check(app.Snapshot().State == before.State && app.Snapshot().Configuration == before.Configuration);
        });
    }
}
