using System.Text.Json;
using System.Text.Json.Nodes;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;
using static Program;

internal static class InteractiveHostRegressions
{
    private static MonitoringApplication Interactive(Fixture f, ReminderStyle style = ReminderStyle.InteractiveWindow, long repeat = 60_000)
    {
        f.Enable(); f.Settings.Save(f.Settings.Load() with { ReminderStyle = style });
        using (var session = f.Session()) session.ChangeConfiguration(session.Configuration with
        { AllowanceMs = 2_000, RequiredBreakMs = 600_000, InteractiveRepeatNudgeMs = repeat });
        var app = f.App();
        for (var ms = 0; ms <= 3_000; ms += 1_000) f.Advance(app, ms);
        return app;
    }
    private static ReminderEvent Take(MonitoringApplication app)
    { Check(app.Reminders.TryTake(Environment.TickCount64, out var value)); Check(value is not null); return value!; }
    private static HostCommand Authorize(ReminderEvent value) => new(HostCommandKind.AuthorizeNudge) { Reminder = value, NudgeWindow = 2 };
    internal static void Run()
    {
        foreach (var style in new[] { ReminderStyle.WindowsNotification, ReminderStyle.InteractiveWindow })
            foreach (var repeat in new[] { 60_000L, 120_000L })
                Test($"{style} prompts at committed exhaustion and repeats at shared {repeat} interval", () =>
                {
                    using var f = new Fixture(); using var app = Interactive(f, style, repeat);
                    var value = Take(app);
                    Check(value.Style == style && app.Snapshot().State!.UsageMs == 2_000);
                    for (var ms = 4_000L; ms <= 3_000 + repeat; ms += 1_000)
                    {
                        f.Advance(app, ms);
                        Check(app.Reminders.TryTake(Environment.TickCount64, out var next) == (ms == 3_000 + repeat));
                        if (next is not null) Check(next.Style == style && next.Sequence > value.Sequence);
                    }
                });
        Test("exhausted style transitions preserve cadence and never replay at Settings save", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); Take(app);
            var before = app.Snapshot().State;
            foreach (var style in new[] { ReminderStyle.WindowsNotification, ReminderStyle.Off, ReminderStyle.InteractiveWindow })
            {
                Check(app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = style }).Succeeded);
                Check(app.Snapshot().State == before && !app.Reminders.TryTake(Environment.TickCount64));
            }
            Check(app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = ReminderStyle.WindowsNotification }).Succeeded);
            for (var ms = 4_000; ms <= 63_000; ms += 1_000) f.Advance(app, ms);
            Check(Take(app).Style == ReminderStyle.WindowsNotification);
        });
        Test("switching an active interactive extension to notification preserves its Personal-only remainder", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f);
            Check(app.Execute(Authorize(Take(app))).Succeeded);
            Check(app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 60_000 }).Succeeded);
            var before = app.Snapshot().State;
            Check(app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = ReminderStyle.WindowsNotification }).Succeeded);
            Check(app.Snapshot().State == before && !app.Reminders.TryTake(Environment.TickCount64));
            f.Source!.Current = f.Work; f.Source.Emit(SignalKind.InputDesktopChanged);
            for (var ms = 4_000; ms <= 7_000; ms += 1_000) f.Advance(app, ms);
            Check(app.Snapshot().State!.SnoozeRemainingMs == 60_000);
            Check(!app.Reminders.TryTake(Environment.TickCount64));
        });
        Test("interactive mailbox waits for matching publication without consuming or renewing the event", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); var current = app.Snapshot();
            var old = current with { State = current.State! with { EffectSequence = 0 }, CanKeepNudgeOpen = false };
            var now = Environment.TickCount64;
            Check(!app.Reminders.TryTakePublished(now, old, out _));
            Check(app.Reminders.TryTakePublished(now, current, out var value) && value is not null);
            Check(!app.Reminders.TryTakePublished(now, current, out _));
        });
        Test("publication waiting never replays an expired interactive event", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); var current = app.Snapshot();
            var now = Environment.TickCount64;
            Check(!app.Reminders.TryTakePublished(now, null, out _));
            Check(!app.Reminders.TryTakePublished(now + 2_001, current, out _));
            Check(!app.Reminders.TryTakePublished(now, current, out _));
        });
        Test("preparation retains original committed event for strict post-show authorization", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); var value = Take(app);
            Check(app.Snapshot().ReminderStatus.Contains("Committed nudge offered"));
            Check(app.Execute(Authorize(value) with { PrepareOnly = true }).Succeeded);
            Check(app.Execute(Authorize(value)).Succeeded);
            Check(f.Source!.NudgeCheckPhases.SequenceEqual(new[] { true, false }));
            Check(!app.Execute(Authorize(value)).Succeeded);
        });
        Test("worker rejection category reaches authorization result without identities", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); var value = Take(app);
            f.Source!.NudgeCheckOverride = NudgeDesktopCheck.CandidateIdentityUnavailable;
            var result = app.Execute(Authorize(value));
            Check(!result.Succeeded && result.Message == "Desktop check: CandidateIdentityUnavailable.");
            Check(!result.Message.Contains(f.Personal.ToString(), StringComparison.Ordinal));
        });
        Test("v2 on/off strictly migrates to notification/off without writing or enabling interactive", () =>
        {
            using var f = new Fixture(); f.Enable(); var path = Path.Combine(f.PathName, "windows-settings.json");
            foreach (var enabled in new[] { false, true })
            {
                var original = f.Settings.Load(); var node = JsonSerializer.SerializeToNode(original)!;
                node["Version"] = 2; node.AsObject().Remove("ReminderStyle"); node["BreakRemindersEnabled"] = enabled;
                File.WriteAllText(path, node.ToJsonString()); var bytes = File.ReadAllBytes(path);
                var migrated = f.Settings.Load();
                Check(migrated == original with { ReminderStyle = enabled ? ReminderStyle.WindowsNotification : ReminderStyle.Off });
                Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
            }
            foreach (var bad in new[] { -1, 3, int.MaxValue })
            { var node = JsonSerializer.SerializeToNode(new WindowsSettings())!; node["ReminderStyle"] = bad; File.WriteAllText(path, node.ToJsonString()); f.Settings.Load(); Check(f.Settings.Status == SettingsStatus.Invalid); }
        });
        Test("interactive mailbox offers committed immediate event and one authorization only", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f);
            Check(app.Snapshot().State!.UsageMs == 2_000 && app.Snapshot().CanKeepNudgeOpen);
            var value = Take(app); Check(value.Style == ReminderStyle.InteractiveWindow && value.ForegroundWindow == 1);
            Check(app.Execute(Authorize(value)).Succeeded);
            Check(!app.Execute(Authorize(value)).Succeeded);
            Check(app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 300_000 }).Succeeded);
            Check(app.Snapshot().State!.Intervention == InterventionState.PersonalDeferred && !app.Snapshot().CanKeepNudgeOpen);
            Check(app.Snapshot().State!.UsageMs == 2_000);
        });
        foreach (var reason in new[] { "desktop", "signal", "stale", "style", "command", "expired", "wrong-window" })
            Test("interactive authorization suppresses " + reason, () =>
            {
                using var f = new Fixture(); using var app = Interactive(f); var value = Take(app);
                switch (reason)
                {
                    case "desktop": f.Source!.NudgeDesktopValid = false; break;
                    case "signal": f.Source!.OnNudgeCheck = () => f.Source.Emit(SignalKind.InputDesktopChanged); break;
                    case "stale": f.Clock.Now = f.Clock.Now with { Milliseconds = 10_000 }; break;
                    case "style": app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = ReminderStyle.Off }); break;
                    case "command": app.Execute(new(HostCommandKind.ChangeAppearance) { Theme = AppTheme.Dark }); break;
                    case "expired": value = value with { ExpiresTick = Environment.TickCount64 - 1 }; break;
                    case "wrong-window": value = value with { ForegroundWindow = 99 }; break;
                }
                Check(!app.Execute(Authorize(value)).Succeeded);
            });
        Test("own foreground transition keeps visibility while old event is revoked and later repeats remain eligible", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); var value = Take(app);
            Check(app.Execute(Authorize(value)).Succeeded);
            f.Source!.Application = new("offramp-self", true); f.Source.Emit(SignalKind.Foreground); app.Step();
            Check(app.Snapshot().CanKeepNudgeOpen);
            ReminderEvent? repeated = null;
            for (var ms = 4_000; ms <= 70_000; ms += 1_000)
            {
                f.Advance(app, ms);
                if (app.Reminders.TryTake(Environment.TickCount64, out repeated)) break;
            }
            Check(repeated is not null && repeated.Sequence > value.Sequence);
        });
        Test("context changes reject stale extra-time commands and repeat edits preserve break state", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); Check(app.Execute(Authorize(Take(app))).Succeeded);
            f.Source!.Current = f.Work; f.Advance(app, 4_000);
            Check(!app.Snapshot().CanKeepNudgeOpen);
            Check(!app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 60_000 }).Succeeded);
            f.Advance(app, 5_000); f.Advance(app, 6_000); var before = app.Snapshot().State;
            Check(app.Execute(new(HostCommandKind.ChangeInteractiveRepeat) { DurationMs = 120_000 }).Succeeded);
            Check(app.Snapshot().State == before && app.Snapshot().Configuration!.InteractiveRepeatNudgeMs == 120_000);
        });
        Test("extra time revalidates current context before the next runtime step", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); Check(app.Execute(Authorize(Take(app))).Succeeded);
            f.Source!.Current = f.Work; f.Source.Emit(SignalKind.InputDesktopChanged);
            Check(!app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 60_000 }).Succeeded);
            Check(app.Snapshot().State!.Intervention == InterventionState.Nudging);
        });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test("interactive extra-time failed save withholds state and retry does not replay: " + stage, () =>
            {
                using var f = new Fixture(); using var app = Interactive(f); Check(app.Execute(Authorize(Take(app))).Succeeded);
                f.Fault = point => { if (point == stage) throw new IOException(); };
                Check(!app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 60_000 }).Succeeded);
                Check(app.Snapshot().Persistence == PersistenceStatus.PendingCommit && app.Snapshot().State is null);
                f.Fault = null; Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
                Check(app.Snapshot().State!.SnoozeRemainingMs == 60_000 && !app.Reminders.TryTake(Environment.TickCount64));
            });
    }
}
