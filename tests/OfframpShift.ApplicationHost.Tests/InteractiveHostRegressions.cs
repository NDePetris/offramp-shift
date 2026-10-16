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
    private static HostCommand Authorize(ReminderEvent value) => new(HostCommandKind.BeginNudgePresentation) { Reminder = value, NudgeWindow = 2 };
    private static HostCommandResult AuthorizeFully(MonitoringApplication app, ReminderEvent value)
    {
        var begin = app.Execute(Authorize(value));
        return begin.Succeeded ? app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = begin.Presentation, NudgeWindow = 2 }) : begin;
    }
    internal static void Run()
    {
        foreach (var fault in new[] { "health", "shell" })
            Test("published focus confirmation is revoked by subsequent " + fault + " generation", () =>
            {
                using var f = new Fixture(); using var app = Interactive(f);
                var begin = app.Execute(Authorize(Take(app)));
                Check(app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = begin.Presentation, NudgeWindow = 2 }).Succeeded);
                Check(app.Snapshot().PresentationId == begin.Presentation!.Id);
                if (fault == "health") f.Source!.Dropped++; else f.Source!.ShellGeneration++;
                f.Advance(app, 4_000);
                Check(app.Snapshot().CanKeepNudgeOpen && app.Snapshot().PresentationId == Guid.Empty);
            });
        Test("window creation destruction callbacks occur inside the accepted transaction without suppressing its trigger", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f);
            var begin = app.Execute(Authorize(Take(app)));
            Check(begin.Succeeded && begin.Presentation is not null);
            var generation = app.Snapshot().ReminderGeneration;
            f.Source!.Emit(SignalKind.Destroyed, window: 99);
            f.Advance(app, 4_000);
            Check(app.Snapshot().ReminderGeneration != generation);
            Check(app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = begin.Presentation, NudgeWindow = 2 }).Succeeded);
        });
        foreach (var multiple in new[] { false, true })
            Test("provisional foreground is never trusted before binding the owned candidate: multiple=" + multiple, () =>
            {
                using var f = new Fixture(); using var app = Interactive(f);
                var begin = app.Execute(Authorize(Take(app)));
                f.Source!.Emit(SignalKind.Foreground, window: 99);
                if (multiple) f.Source.Emit(SignalKind.Foreground, window: 2);
                f.Source.Emit(SignalKind.Foreground, window: 1);
                f.Advance(app, 4_000); // Drain before the UI supplies its actual HWND.
                var result = app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = begin.Presentation, NudgeWindow = 2 });
                Check(!result.Succeeded && result.Message == "ExternalForegroundSignal");
            });
        foreach (var current in new nint[] { 1, 2 })
            Test($"owned DPI foreground signal permits fresh strict confirmation with current window role {current}", () =>
            {
                using var f = new Fixture(); using var app = Interactive(f); var value = Take(app);
                var begin = app.Execute(Authorize(value));
                Check(begin.Succeeded && begin.Presentation is not null);
                var generation = app.Snapshot().ReminderGeneration;
                f.Source!.Window = current;
                f.Source.Emit(SignalKind.Foreground, window: 2);
                f.Advance(app, 4_000); // Accounting is interrupted; do not ignore its signal.
                Check(app.Snapshot().ReminderGeneration != generation && app.Snapshot().Status == "Waiting for evidence");
                var completed = app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = begin.Presentation, NudgeWindow = 2 });
                Check(completed.Succeeded && app.Snapshot().PresentationId == begin.Presentation!.Id);
                Check(!app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = begin.Presentation, NudgeWindow = 2 }).Succeeded);
            });
        foreach (var reason in new[] { "external", "external-and-return", "desktop", "session", "power", "destroyed", "drops",
            "work-no-signal", "lock-no-signal", "unknown", "shell", "wrong-member", "unregistered-member", "pause", "style", "expiry", "forged", "during-check" })
            Test("presentation transaction independently suppresses " + reason, () =>
            {
                using var f = new Fixture(); using var app = Interactive(f);
                var begin = app.Execute(Authorize(Take(app)));
                Check(begin.Succeeded && begin.Presentation is not null);
                var ticket = begin.Presentation!;
                switch (reason)
                {
                    case "external": f.Source!.Emit(SignalKind.Foreground, window: 99); break;
                    case "external-and-return": f.Source!.Emit(SignalKind.Foreground, window: 99); f.Source.Emit(SignalKind.Foreground, window: 1); break;
                    case "desktop": f.Source!.Emit(SignalKind.InputDesktopChanged); break;
                    case "session": f.Source!.Emit(SignalKind.Session); break;
                    case "power": f.Source!.Emit(SignalKind.Power); break;
                    case "destroyed": f.Source!.Emit(SignalKind.Destroyed, window: 1); break;
                    case "drops": f.Source!.Dropped++; break;
                    case "work-no-signal": f.Source!.Current = f.Work; break;
                    case "lock-no-signal": f.Source!.Session = SessionState.Locked; break;
                    case "unknown": f.Source!.Foreground = Capability.Unavailable; break;
                    case "shell": f.Source!.ShellGeneration++; break;
                    case "wrong-member": f.Source!.NudgeCheckOverride = NudgeDesktopCheck.CandidateWrongDesktop; break;
                    case "unregistered-member": f.Source!.NudgeCheckOverride = NudgeDesktopCheck.CandidateIdentityUnavailable; break;
                    case "pause": app.Execute(new(HostCommandKind.Pause)); break;
                    case "style": app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = ReminderStyle.WindowsNotification }); break;
                    case "expiry": f.Clock.Now = f.Clock.Now with { Milliseconds = f.Ms + 2_001 }; f.Source!.Tick(); break;
                    case "forged": ticket = ticket with { Id = Guid.NewGuid() }; break;
                    case "during-check": f.Source!.OnNudgeCheck = () => f.Source.Emit(SignalKind.Foreground, window: 99); break;
                }
                var rejected = app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = ticket, NudgeWindow = 2 });
                Check(!rejected.Succeeded);
                Check(!rejected.Message.Contains(f.Personal.ToString(), StringComparison.Ordinal));
                if (reason == "expiry") Check(rejected.Message == "PresentationExpired");
            });
        Test("preparation is not focus authorization and does not grant extra Personal time", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f);
            var begin = app.Execute(Authorize(Take(app)));
            Check(begin.Succeeded);
            Check(!app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 60_000 }).Succeeded);
            Check(app.Snapshot().State!.Intervention == InterventionState.Nudging);
        });
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
            Check(AuthorizeFully(app, Take(app)).Succeeded);
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
        Test("acceptance consumes the committed event and completion verifies the visible candidate strictly", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); var value = Take(app);
            Check(app.Snapshot().ReminderStatus.Contains("Committed nudge offered"));
            var begin = app.Execute(Authorize(value));
            Check(begin.Succeeded && begin.Presentation is not null);
            Check(app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = begin.Presentation, NudgeWindow = 2 }).Succeeded);
            Check(f.Source!.NudgeCheckPhases.SequenceEqual(new[] { false, false }));
            Check(!AuthorizeFully(app, value).Succeeded);
        });
        Test("worker rejection category reaches authorization result without identities", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); var value = Take(app);
            f.Source!.NudgeCheckOverride = NudgeDesktopCheck.CandidateIdentityUnavailable;
            var result = AuthorizeFully(app, value);
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
            Check(AuthorizeFully(app, value).Succeeded);
            Check(!AuthorizeFully(app, value).Succeeded);
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
                Check(!AuthorizeFully(app, value).Succeeded);
            });
        Test("own foreground transition keeps visibility while old event is revoked and later repeats remain eligible", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); var value = Take(app);
            Check(AuthorizeFully(app, value).Succeeded);
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
            using var f = new Fixture(); using var app = Interactive(f); Check(AuthorizeFully(app, Take(app)).Succeeded);
            f.Source!.Current = f.Work; f.Advance(app, 4_000);
            Check(!app.Snapshot().CanKeepNudgeOpen);
            Check(!app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 60_000 }).Succeeded);
            f.Advance(app, 5_000); f.Advance(app, 6_000); var before = app.Snapshot().State;
            Check(app.Execute(new(HostCommandKind.ChangeInteractiveRepeat) { DurationMs = 120_000 }).Succeeded);
            Check(app.Snapshot().State == before && app.Snapshot().Configuration!.InteractiveRepeatNudgeMs == 120_000);
        });
        Test("extra time revalidates current context before the next runtime step", () =>
        {
            using var f = new Fixture(); using var app = Interactive(f); Check(AuthorizeFully(app, Take(app)).Succeeded);
            f.Source!.Current = f.Work; f.Source.Emit(SignalKind.InputDesktopChanged);
            Check(!app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 60_000 }).Succeeded);
            Check(app.Snapshot().State!.Intervention == InterventionState.Nudging);
        });
        foreach (var stage in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            Test("interactive extra-time failed save withholds state and retry does not replay: " + stage, () =>
            {
                using var f = new Fixture(); using var app = Interactive(f); Check(AuthorizeFully(app, Take(app)).Succeeded);
                f.Fault = point => { if (point == stage) throw new IOException(); };
                Check(!app.Execute(new(HostCommandKind.DeferInteractive) { NudgeWindow = 2, DurationMs = 60_000 }).Succeeded);
                Check(app.Snapshot().Persistence == PersistenceStatus.PendingCommit && app.Snapshot().State is null);
                f.Fault = null; Check(app.Execute(new(HostCommandKind.RetrySave)).Succeeded);
                Check(app.Snapshot().State!.SnoozeRemainingMs == 60_000 && !app.Reminders.TryTake(Environment.TickCount64));
            });
    }
}
