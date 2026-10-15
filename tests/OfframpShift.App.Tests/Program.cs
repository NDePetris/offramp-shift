using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;

internal static class Program
{
    private static int passed, failed, assertions;
    private static void Check(bool condition) { assertions++; if (!condition) throw new InvalidOperationException("Assertion failed"); }
    private static void Test(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL: " + name + " " + e.Message); }
    }
    private static HostSnapshot Snapshot(string status = "Monitoring", bool paused = false, PersistenceStatus persistence = PersistenceStatus.Ready) =>
        new(1, Environment.TickCount64, persistence, null, SettingsStatus.Ready,
            new() { Consent = true, MonitoringEnabled = true, Personal = Guid.NewGuid(), Work = Guid.NewGuid(), Build = "26200.9457" },
            persistence == PersistenceStatus.Ready ? new(65_000, status == "Allowance exhausted", 12_000, false, 0, paused, status == "Nudging" ? InterventionState.Nudging : InterventionState.Normal, 0, 0, 0, RecoveryStatus.Ready) : null,
            persistence == PersistenceStatus.Ready ? new() { Accounting = AccountingMode.BestEffort } : null, DesktopScope.Personal, status,
            "Estimated monitoring. Unknown intervals earn no credit.", true, true, 0, true, false, true,
            AssignmentStatus.Valid, AssignmentStatus.Valid);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--instance-client", var suffix])
        { using var client = new SingleInstance(suffix); return !client.IsOwner && client.Activate() ? 0 : 1; }
        Test("setup projection identifies next prerequisite without exposing desktop identities", () =>
        {
            var model = new DashboardModel(); var ready = Snapshot();
            var initial = ready with
            {
                Settings = new(),
                ObservationAllowed = false,
                CanConsent = true,
                CanAssign = false,
                PersonalAssignment = AssignmentStatus.Unassigned,
                WorkAssignment = AssignmentStatus.Unassigned,
                BindingsValid = false
            };
            model.Apply(initial, initial.PublishedTick);
            Check(model.CanConsent && !model.CanAssign && !model.CanStart && model.NextSetupAction.Contains("agree"));
            Check(model.PersonalStatus.Contains("unassigned") && model.WorkStatus.Contains("unassigned"));
            var personal = initial with
            {
                Settings = ready.Settings with { Work = null, MonitoringEnabled = false },
                ObservationAllowed = true,
                CanConsent = false,
                CanAssign = true,
                PersonalAssignment = AssignmentStatus.Valid
            };
            model.Apply(personal, personal.PublishedTick);
            Check(model.NextSetupAction.Contains("Work desktop") && model.CanAssign && !model.CanStart);
            var complete = ready with { Settings = ready.Settings with { MonitoringEnabled = false } };
            model.Apply(complete, complete.PublishedTick);
            Check(model.CanStart && model.NextSetupAction == "Next: select Start monitoring.");
            Check(model.Bindings.Contains("assigned and observed") && !model.Bindings.Contains(ready.Settings.Personal!.ToString()!));
            model.Apply(ready, ready.PublishedTick); Check(model.StartStatus.Contains("Completed") && !model.CanStart);
            model.Apply(complete with { WorkAssignment = AssignmentStatus.Missing, BindingsValid = false }, complete.PublishedTick);
            Check(model.WorkStatus.Contains("Invalid") && model.NextSetupAction.Contains("Work desktop") && !model.CanStart);
            model.Apply(complete with { CanAssign = false, BindingsValid = false, PersonalAssignment = AssignmentStatus.Unavailable }, complete.PublishedTick);
            Check(!model.CanAssign && model.PersonalStatus.Contains("awaiting fresh validation"));
            model.Apply(complete, complete.PublishedTick + 2_001);
            Check(!model.CanStart && !model.CanConsent && !model.CanAssign && model.NextSetupAction.Contains("stale"));
            model.Apply(initial with { CanConsent = false, SettingsStatus = SettingsStatus.Incompatible }, initial.PublishedTick);
            Check(!model.CanConsent && model.ConsentStatus.Contains("Unavailable"));
            model.Apply(ready with { ConsentNeedsReview = true, CanConsent = true, ObservationAllowed = false }, ready.PublishedTick);
            Check(model.CanConsent && model.ConsentStatus.Contains("review for this Windows build") && model.NextSetupAction.Contains("agree"));
            model.Apply(ready with
            {
                Settings = new(),
                PersonalAssignment = AssignmentStatus.Unavailable,
                WorkAssignment = AssignmentStatus.Unavailable,
                ObservationAllowed = false,
                CanAssign = false,
                SettingsStatus = SettingsStatus.Invalid
            }, ready.PublishedTick);
            Check(model.PersonalStatus.Contains("Unavailable") && model.WorkStatus.Contains("Unavailable"));
        });
        Test("tray command pipeline retains rejected actions deduplicates alerts and stays quiet on success", () =>
        {
            var feedback = new TrayActionFeedback();
            foreach (var kind in new[] { HostCommandKind.AssignPersonal, HostCommandKind.AssignWork, HostCommandKind.RetryMonitoring,
                HostCommandKind.RetrySave, HostCommandKind.Pause, HostCommandKind.Resume })
            {
                var error = kind + ": action rejected; review setup and retry.";
                Task<HostCommandResult> Execute(HostCommand command)
                { Check(command.Kind == kind); return Task.FromResult(HostCommandResult.Failure(error)); }
                Check(feedback.ExecuteAsync(new(kind), Execute).GetAwaiter().GetResult());
                Check(feedback.Message == error);
                Check(!feedback.ExecuteAsync(new(kind), Execute).GetAwaiter().GetResult());
                // A successful retry clears that action's failure without generating another alert.
                Check(!feedback.ExecuteAsync(new(kind), _ => Task.FromResult(HostCommandResult.Success("Saved."))).GetAwaiter().GetResult());
                Check(feedback.Message.Length == 0);
                Check(feedback.ExecuteAsync(new(kind), Execute).GetAwaiter().GetResult());
                feedback.Dismiss(); Check(feedback.Message.Length == 0);
            }
        });
        Test("first close stays open on either failed choice and saves only the close preference", () =>
        {
            foreach (var choice in new[] { true, false })
            {
                var reports = new List<string>(); var attempts = 0;
                Task<HostCommandResult> Execute(HostCommand command)
                {
                    attempts++; Check(command.Kind == HostCommandKind.ChangeCloseBehavior && command.CloseToTray == choice);
                    return Task.FromResult(attempts == 1 ? HostCommandResult.Failure("Check local storage access.") : HostCommandResult.Success());
                }
                Check(ClosePreference.ResolveAsync(null, choice, Execute, reports.Add).GetAwaiter().GetResult() is null);
                Check(reports.Count == 1 && reports[0].Contains("window will stay open") && reports[0].Contains("Close again") && reports[0].Contains("Exit Offramp Shift"));
                Check(ClosePreference.ResolveAsync(null, choice, Execute, reports.Add).GetAwaiter().GetResult() == choice && attempts == 2);
                Check(ClosePreference.ResolveAsync(choice, null, Execute, reports.Add).GetAwaiter().GetResult() == choice && attempts == 2);
                Check(ClosePreference.ResolveAsync(null, null, Execute, reports.Add).GetAwaiter().GetResult() is null && attempts == 2);
            }
        });
        Test("dashboard distinguishes monitoring paused unavailable exhaustion and nudging", () =>
        {
            var model = new DashboardModel();
            foreach (var status in new[] { "Monitoring", "Paused", "Unavailable", "Allowance exhausted", "Nudging" })
            { model.Apply(Snapshot(status, status == "Paused"), Environment.TickCount64); Check(model.Status == status); }
        });
        Test("stale presentation freezes committed values and disables accounting commands", () =>
        {
            var model = new DashboardModel(); var snapshot = Snapshot(); model.Apply(snapshot, snapshot.PublishedTick);
            var usage = model.Usage; var progress = model.BreakProgress;
            model.Apply(snapshot, snapshot.PublishedTick + 5_000);
            Check(model.Status == "Unavailable" && model.Context == "Unknown");
            Check(model.Usage == usage && model.BreakProgress == progress); Check(!model.CanStart && !model.CanResume && !model.CanPause);
            model.MarkUnavailable("Startup failed"); Check(model.Status == "Unavailable"); Check(model.Detail == "Startup failed");
        });
        Test("persistence actions cannot substitute for one another or reset blocked/future data", () =>
        {
            var model = new DashboardModel();
            foreach (var persistence in new[] { PersistenceStatus.PendingCommit, PersistenceStatus.RecoveryRequired, PersistenceStatus.Incompatible, PersistenceStatus.Blocked })
            {
                model.Apply(Snapshot(persistence: persistence), Environment.TickCount64);
                Check(model.CanRetrySave == (persistence == PersistenceStatus.PendingCommit));
                Check(model.CanRecover == (persistence == PersistenceStatus.RecoveryRequired));
                Check(model.CanReopen == (persistence == PersistenceStatus.Blocked));
                Check(!model.CanEdit && model.Usage == "Saved usage unavailable");
            }
        });
        Test("duration parsing rejects overflow negative values and excess precision", () =>
        {
            Check(MainWindow.ParseDuration("30", 60_000, false, out var duration) && duration == 1_800_000);
            Check(MainWindow.ParseDuration("0", 1_000, true, out _));
            foreach (var text in new[] { "-1", "0", "9999999999999999999999999999", "invalid" })
                Check(!MainWindow.ParseDuration(text, 60_000, false, out _));
        });
        Test("same-user second process activates owner without becoming another owner", () =>
        {
            var suffix = Guid.NewGuid().ToString("N"); using var owner = new SingleInstance(suffix);
            using var activated = new ManualResetEventSlim(); owner.Listen(activated.Set);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--instance-client"); start.ArgumentList.Add(suffix);
            using var client = Process.Start(start)!;
            Check(client.WaitForExit(5_000)); Check(client.ExitCode == 0); Check(activated.Wait(2_000));
        });
        Test("actual WinExe worker fault modes use redirected pipes without creating UI or COM", () =>
        {
            foreach (var mode in new[] { "--worker-malformed", "--worker-hang" })
            {
                var start = new ProcessStartInfo(Path.ChangeExtension(typeof(OfframpShift.App.Program).Assembly.Location, ".exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add(mode);
                using var worker = Process.Start(start)!; worker.StandardInput.WriteLine("read"); worker.StandardInput.Flush();
                if (mode == "--worker-malformed")
                { Check(worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == "{broken}"); Check(worker.WaitForExit(3_000)); }
                else { Check(!worker.WaitForExit(200)); worker.Kill(); Check(worker.WaitForExit(3_000)); }
            }
        });
        var app = new ShiftApplication();
        Test("canonical symbols and all seven native icon sizes decode from packaged resources", () =>
        {
            foreach (var dark in new[] { false, true })
            {
                var symbol = BrandAssets.Symbol(dark);
                Check(symbol.PixelWidth == 128 && symbol.PixelHeight == 128 && symbol.IsFrozen);
                var frames = BrandAssets.WindowIcon(dark).Decoder!.Frames;
                Check(frames.Count == 7);
                Check(frames.Select(frame => frame.PixelWidth).ToHashSet().SetEquals([16, 24, 32, 48, 64, 128, 256]));
                // NotifyIcon uses 32px. Drawing.Icon can choose 128px when asked
                // for 256px; verify the retained 256px frame with WPF above.
                foreach (var size in new[] { 16, 24, 32, 48, 64, 128 })
                {
                    using var icon = BrandAssets.LoadTrayIcon(dark, size);
                    Check(icon.Width == size && icon.Height == size);
                }
            }
        });
        Test("family artwork adapts without recoloring and stays decorative in high contrast", () =>
        {
            var window = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            Check(!BrandAssets.IsDark(Brushes.White) && BrandAssets.IsDark(Brushes.Black));
            window.ApplyBrand(false, false);
            Check(ReferenceEquals(window.FamilySymbol.Source, BrandAssets.Symbol(false)));
            Check(ReferenceEquals(window.Icon, BrandAssets.WindowIcon(false)));
            window.ApplyBrand(true, false);
            Check(ReferenceEquals(window.FamilySymbol.Source, BrandAssets.Symbol(true)));
            Check(ReferenceEquals(window.Icon, BrandAssets.WindowIcon(true)));
            window.ApplyBrand(true, true);
            Check(window.FamilySymbol.Visibility == Visibility.Collapsed && window.Title == "Offramp Shift");
            Check(UIElementAutomationPeer.CreatePeerForElement(window.FamilySymbol) is null);
        });
        Test("native WPF views load bind and expose accessible labels without live monitoring", () =>
        {
            var window = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            window.Apply(Snapshot());
            var root = (FrameworkElement)window.Content;
            root.Measure(new(820, 760)); root.Arrange(new(0, 0, 820, 760)); root.UpdateLayout();
            Check(window.Model.Usage.Contains("1m 5s")); Check(window.Navigation.Items.Count == 2);
            Check(AutomationProperties.GetName(window.AllowanceInput) == "Allowance in minutes");
            var peer = UIElementAutomationPeer.CreatePeerForElement(window.AllowanceInput);
            Check(peer is not null && peer.GetAutomationControlType() == AutomationControlType.Edit);
            Check(window.AllowanceInput.Focusable && window.Navigation.Focusable);
            window.SetupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout(); Check(window.Navigation.SelectedIndex == 1 && window.ThemeInput.Items.Count == 3);
            Check(AutomationProperties.GetLiveSetting(window.SettingsResult) == AutomationLiveSetting.Polite);
            foreach (var button in new[] { window.ConsentButton, window.PersonalButton, window.WorkButton, window.SetupStartButton, window.SetupButton })
            {
                Check(button.Focusable && System.Windows.Input.KeyboardNavigation.GetIsTabStop(button));
                Check(button.Content.ToString()!.Contains('_'));
                var buttonPeer = UIElementAutomationPeer.CreatePeerForElement(button);
                Check(buttonPeer is not null && buttonPeer.GetAutomationControlType() == AutomationControlType.Button && buttonPeer.GetName().Length > 0);
            }
            var fresh = Snapshot() with
            {
                Settings = new(),
                CanConsent = true,
                CanAssign = false,
                BindingsValid = false,
                ObservationAllowed = false,
                PersonalAssignment = AssignmentStatus.Unassigned,
                WorkAssignment = AssignmentStatus.Unassigned
            };
            window.Apply(fresh); root.UpdateLayout();
            Check(window.ConsentButton.IsEnabled && !window.PersonalButton.IsEnabled && !window.WorkButton.IsEnabled && !window.SetupStartButton.IsEnabled);
            window.ReportAction("Desktop assignment rejected. Choose another desktop in Task View.");
            Check(window.ActionResult.Text == window.SettingsResult.Text && window.SettingsResult.Text.Contains("Task View"));
        });
        if (args is ["--render", var output])
        {
            Directory.CreateDirectory(output);
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                app.ApplyTheme(theme);
                foreach (var settings in new[] { false, true })
                    foreach (var scenario in new[] { (Name: "Monitoring", Width: 820, Height: 760), (Name: "Setup", Width: 820, Height: 760), (Name: "SetupSmall", Width: 480, Height: 420) })
                    {
                        var window = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
                        var snapshot = Snapshot();
                        if (scenario.Name != "Monitoring") snapshot = snapshot with
                        {
                            Settings = new(),
                            ObservationAllowed = false,
                            CanConsent = true,
                            CanAssign = false,
                            BindingsValid = false,
                            PersonalAssignment = AssignmentStatus.Unassigned,
                            WorkAssignment = AssignmentStatus.Unassigned,
                            Context = DesktopScope.Unknown,
                            State = snapshot.State! with { UsageMs = 0, EstimatedUsageMs = 0, BreakProgressMs = 0 },
                            Configuration = new(),
                            Status = "Setup required",
                            Detail = "Opt in to experimental Estimated monitoring to observe and assign desktops."
                        };
                        window.Apply(snapshot); window.Navigation.SelectedIndex = settings ? 1 : 0;
                        var content = (FrameworkElement)window.Content;
                        var bounds = new Rect(0, 0, scenario.Width, scenario.Height);
                        content.Measure(bounds.Size); content.Arrange(bounds); content.UpdateLayout();
                        var drawing = new DrawingVisual();
                        using (var dc = drawing.RenderOpen())
                        {
                            dc.DrawRectangle(window.Background ?? SystemColors.WindowBrush, null, bounds);
                            dc.DrawRectangle(new VisualBrush(content), null, bounds);
                        }
                        var image = new RenderTargetBitmap(scenario.Width, scenario.Height, 96, 96, PixelFormats.Pbgra32); image.Render(drawing);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                        using var file = File.Create(Path.Combine(output, $"{theme}-{scenario.Name}-{(settings ? "Settings" : "Home")}.png")); encoder.Save(file);
                    }
            }
        }
        Console.WriteLine($"App: {passed} passed, {failed} failed, {assertions} assertions (synthetic presentation and non-COM Windows process tests).");
        return failed == 0 ? 0 : 1;
    }
}
