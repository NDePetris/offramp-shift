using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopFeasibility;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using WindowsObservation;
using static Program;

internal static class SwitchRegressions
{
    internal static MonitoringApplication Active(Fixture f, string build = "26200.9457")
    {
        f.Enable(build: build);
        f.Settings.Save(f.Settings.Load() with
        {
            ReminderStyle = ReminderStyle.InteractiveWindow,
            SwitchingConsent = true,
            SwitchingConsentBuild = SwitchCompatibility.Is26300Family(build) ? "" : build,
            SwitchingConsentFamily = SwitchCompatibility.Is26300Family(build) ? "26300" : ""
        });
        using (var session = f.Session()) session.ChangeConfiguration(session.Configuration with { AllowanceMs = 2_000 });
        var app = f.App(build: build, switchConfiguration: () => new(build, "X64", 1, 2));
        for (var ms = 0; ms <= 3_000; ms += 1_000) f.Advance(app, ms);
        Check(app.Reminders.TryTake(Environment.TickCount64, out var reminder));
        var begin = app.Execute(new(HostCommandKind.BeginNudgePresentation) { Reminder = reminder });
        Check(begin.Succeeded);
        Check(app.Execute(new(HostCommandKind.CompleteNudgePresentation) { Presentation = begin.Presentation, NudgeWindow = 2 }).Succeeded);
        f.Source!.Window = 2; f.Source.UseRealUptime = true; f.Advance(app, 4_000);
        return app;
    }
    private static HostCommand Authorize(nint window = 2) => new(HostCommandKind.AuthorizeSwitch) { NudgeWindow = window };

    internal static int Fault(string mode)
    {
        Console.In.ReadToEnd();
        if (mode == "--switch-fault-hang") { Thread.Sleep(10_000); return 0; }
        if (mode == "--switch-fault-crash") return 23;
        if (mode == "--switch-fault-large") { Console.Write(new string('x', 70_000)); return 0; }
        Console.Write("{broken}"); return 0;
    }
    internal static void Run()
    {
        Test("v3 migration preserves PR9 preferences and accounting bytes with default-off switching", () =>
        {
            using var f = new Fixture(); f.Enable();
            var original = f.Settings.Load() with { ReminderStyle = ReminderStyle.InteractiveWindow, Theme = AppTheme.Dark, CloseToTray = true };
            var node = JsonSerializer.SerializeToNode(original)!.AsObject();
            node["Version"] = 3; node.Remove("SwitchingConsent"); node.Remove("SwitchingConsentBuild"); node.Remove("SwitchingConsentFamily");
            var path = Path.Combine(f.PathName, "windows-settings.json"); File.WriteAllText(path, node.ToJsonString());
            var bytes = File.ReadAllBytes(path);
            var accounting = Directory.GetFiles(f.PathName).Where(p => p != path).ToDictionary(p => p, File.ReadAllBytes);
            Check(f.Settings.Load() == original && f.Settings.Status == SettingsStatus.Ready);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes));
            foreach (var file in accounting) Check(File.ReadAllBytes(file.Key).SequenceEqual(file.Value));
            node["SwitchingConsent"] = true; File.WriteAllText(path, node.ToJsonString());
            f.Settings.Load(); Check(f.Settings.Status == SettingsStatus.Invalid);
        });
        Test("fresh single-use authorization leaves Core configuration state and estimator unchanged", () =>
        {
            using var f = new Fixture(); using var app = Active(f);
            var before = app.Snapshot(); var diagnostics = JsonSerializer.Serialize(app.ObservationDiagnostics);
            var reads = f.Source!.Reads;
            var granted = app.Execute(Authorize()); Check(granted.Succeeded && granted.Switching is not null);
            var after = app.Snapshot(); Check(after.State == before.State && after.Configuration == before.Configuration && after.Settings == before.Settings);
            Check(reads == f.Source.Reads && diagnostics == JsonSerializer.Serialize(app.ObservationDiagnostics));
            var request = granted.Switching!.Consume(Environment.TickCount64); Check(request is { Valid: true });
            Check(request!.Personal == f.Personal && request.Work == f.Work && request.Request.Expected.ForegroundWindow == 2);
            Check(granted.Switching.Consume(Environment.TickCount64) is null);
            var expiry = app.Execute(Authorize()).Switching!;
            Check(expiry.Consume(Environment.TickCount64 + 3_000) is null);
            var revoked = app.Execute(Authorize()).Switching!;
            app.Execute(new(HostCommandKind.ChangeAppearance));
            Check(revoked.Consume(Environment.TickCount64) is null);
        });
        foreach (var change in new[] { "external-return", "session", "power", "destroyed", "provider-failure", "provider-health",
            "shell", "topology", "work", "lock", "presentation" })
            Test("issued switch grant is permanently revoked after " + change, () =>
            {
                using var f = new Fixture(); using var app = Active(f);
                var grant = app.Execute(Authorize()).Switching!; Check(grant is not null);
                switch (change)
                {
                    case "external-return": f.Source!.Emit(SignalKind.Foreground, window: 99); f.Source.Emit(SignalKind.Foreground, window: 2); break;
                    case "session": f.Source!.Emit(SignalKind.Session); break;
                    case "power": f.Source!.Emit(SignalKind.Power); break;
                    case "destroyed": f.Source!.Emit(SignalKind.Destroyed, window: 2); break;
                    case "provider-failure": f.Source!.FailRead = true; break;
                    case "provider-health": f.Source!.Dropped++; break;
                    case "shell": f.Source!.ShellGeneration++; break;
                    case "topology": f.Source!.Topology = [f.Work, f.Personal]; break;
                    case "work": f.Source!.Current = f.Work; break;
                    case "lock": f.Source!.Session = SessionState.Locked; break;
                    case "presentation": app.Execute(new(HostCommandKind.BeginNudgePresentation)); break;
                }
                f.Advance(app, 5_000);
                var attempts = 0;
                if (grant!.Consume(Environment.TickCount64) is not null) attempts++;
                Check(attempts == 0);
                Check(new IsolatedSwitchExecutor().ExecuteAsync(grant).GetAwaiter().GetResult().Outcome == SwitchOutcome.Rejected);
                // Restoring the original foreground cannot restore an old lease.
                Check(grant.Consume(Environment.TickCount64) is null);
            });
        Test("switch availability projection distinguishes consent from normal-build gate", () =>
        {
            using var f = new Fixture(); using var app = Active(f);
            Check(app.Snapshot().SwitchingUnavailableReason == "");
            app.Execute(new(HostCommandKind.ChangeSwitchingConsent));
            Check(app.Snapshot().SwitchingUnavailableReason.Contains("consent", StringComparison.Ordinal));
            using var newer = new Fixture(); using var blocked = Active(newer, "26200.9711");
            Check(blocked.Snapshot().SwitchingUnavailableReason.Contains("not approved", StringComparison.Ordinal));
        });
        foreach (var stop in new[] { false, true })
            Test("unconsumed controller grant revoked on " + (stop ? "shutdown" : "presentation closure"), () =>
            {
                using var f = new Fixture(); var app = Active(f);
                var controller = new MonitoringController(() => app); var executor = new BeforeConsumeExecutor();
                using var cancellation = new CancellationTokenSource();
                var task = controller.SwitchToWorkAsync(2, executor, cancellation.Token);
                Task? shutdown = null;
                try
                {
                    Check(executor.Entered.Wait(3_000));
                    if (stop) shutdown = controller.StopAsync(); else cancellation.Cancel();
                    executor.Release.TrySetResult();
                    Check(task.GetAwaiter().GetResult().Outcome == SwitchOutcome.Rejected && executor.Attempts == 0);
                }
                finally { executor.Release.TrySetResult(); (shutdown ?? controller.StopAsync()).GetAwaiter().GetResult(); }
            });
        foreach (var change in new[] { "build", "consent", "wrong-window", "work", "lock", "foreground", "topology", "shell", "destroyed", "external-return", "session", "power", "stale", "style", "pause", "binding" })
            Test("switch authorization rejects " + change + " without accounting mutation", () =>
            {
                using var f = new Fixture(); using var app = Active(f, change == "build" ? "26200.9711" : "26200.9457");
                switch (change)
                {
                    case "consent": app.Execute(new(HostCommandKind.ChangeSwitchingConsent)); break;
                    case "work": f.Source!.Current = f.Work; f.Advance(app, 5_000); break;
                    case "lock": f.Source!.Session = SessionState.Locked; f.Advance(app, 5_000); break;
                    case "foreground": f.Source!.Window = 3; f.Advance(app, 5_000); break;
                    case "topology": f.Source!.Topology = [f.Personal]; f.Advance(app, 5_000); break;
                    case "shell": f.Source!.ShellGeneration++; f.Advance(app, 5_000); break;
                    case "destroyed": f.Source!.Emit(SignalKind.Destroyed, window: 2); break;
                    case "external-return": f.Source!.Emit(SignalKind.Foreground, window: 3); f.Source.Emit(SignalKind.Foreground, window: 2); break;
                    case "session": f.Source!.Emit(SignalKind.Session); break;
                    case "power": f.Source!.Emit(SignalKind.Power); break;
                    case "stale": f.Clock.Now = f.Clock.Now with { Milliseconds = 8_000 }; break;
                    case "style": app.Execute(new(HostCommandKind.ChangeBreakReminders) { ReminderStyle = ReminderStyle.Off }); break;
                    case "pause": app.Execute(new(HostCommandKind.Pause)); break;
                    case "binding": app.Execute(new(HostCommandKind.AssignPersonal)); break;
                }
                var before = app.Snapshot(); var result = app.Execute(Authorize(change == "wrong-window" ? 3 : 2));
                Check(!result.Succeeded && result.Switching is null);
                var after = app.Snapshot(); Check(before.State == after.State && before.Configuration == after.Configuration);
            });
        Test("controller excludes overlapping switches while monitoring commands continue independently", () =>
        {
            using var f = new Fixture(); var app = Active(f);
            var controller = new MonitoringController(() => app);
            var executor = new DelayedExecutor();
            var first = controller.SwitchToWorkAsync(2, executor);
            try
            {
                Check(executor.Entered.Wait(3_000));
                var overlap = controller.SwitchToWorkAsync(2, executor).GetAwaiter().GetResult();
                Check(overlap.Outcome == SwitchOutcome.Rejected && executor.Calls == 1);
                Check(controller.ExecuteAsync(new(HostCommandKind.Pause)).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult().Succeeded);
                var shutdown = controller.StopAsync(); Check(!shutdown.IsCompleted);
                executor.Result.SetResult(SwitchCompletion.Unknown("Synthetic uncertainty."));
                Check(first.GetAwaiter().GetResult().Outcome == SwitchOutcome.Indeterminate);
                shutdown.GetAwaiter().GetResult();
            }
            finally { executor.Result.TrySetResult(SwitchCompletion.Unknown("Test cleanup.")); controller.StopAsync().GetAwaiter().GetResult(); }
        });
        foreach (var mode in new[] { "hang", "crash", "malformed", "large" })
            Test("owned non-COM switch worker " + mode + " is bounded and reported", () =>
            {
                var timer = System.Diagnostics.Stopwatch.StartNew(); var failed = false;
                try { SwitchWorkerProcess.CallAsync(new("unused"), "--switch-fault-" + mode, mode == "hang" ? 200 : 3_000).GetAwaiter().GetResult(); }
                catch (Exception) { failed = true; }
                Check(failed && timer.ElapsedMilliseconds < 5_000);
            });
        Test("switching consent is build scoped and reassignment revokes it", () =>
        {
            using var f = new Fixture(); using var app = Active(f);
            Check(app.Execute(new(HostCommandKind.AssignPersonal)).Succeeded);
            Check(!app.Snapshot().Settings.SwitchingConsent && app.Snapshot().Settings.SwitchingConsentBuild == "");
            var invalid = app.Snapshot().Settings with { SwitchingConsent = true, SwitchingConsentBuild = "26200.9711" };
            try { invalid.Validate(); Check(false); } catch (ArgumentException) { }
        });
    }
    private sealed class DelayedExecutor : ISwitchExecutor
    {
        internal readonly ManualResetEventSlim Entered = new();
        internal readonly TaskCompletionSource<SwitchCompletion> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        public Task<SwitchCompletion> ExecuteAsync(SwitchAuthorization authorization)
        { Calls++; Check(authorization.Consume(Environment.TickCount64) is not null); Entered.Set(); return Result.Task; }
    }
    private sealed class BeforeConsumeExecutor : ISwitchExecutor
    {
        internal readonly ManualResetEventSlim Entered = new();
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Attempts;
        public async Task<SwitchCompletion> ExecuteAsync(SwitchAuthorization authorization)
        {
            Entered.Set(); await Release.Task;
            if (authorization.Consume(Environment.TickCount64) is null) return SwitchCompletion.Reject("Revoked.");
            Interlocked.Increment(ref Attempts); return SwitchCompletion.Unknown("Synthetic attempt.");
        }
    }
}
