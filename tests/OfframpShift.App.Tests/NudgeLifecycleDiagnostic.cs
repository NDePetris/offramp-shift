using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Xml.Linq;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;
using WindowsObservation;

// Explicit, bounded live exercise. Real host/hooks/worker/presenter; disposable
// accounting only. No user stores, desktop mutation or foreground identities printed.
internal static class NudgeLifecycleDiagnostic
{
    internal static int Run(bool secondary)
    {
        if (!WindowsObservationSource.Supported) { Console.WriteLine("Unsupported build; no exercise started."); return 2; }
        // A plain Application cannot enter ShiftApplication.OnStartup. Load the
        // incumbent resources from this checkout, rather than duplicating styles.
        // Match the already-adopted Fluent ThemeMode in production App.xaml.
#pragma warning disable WPF0001
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
#pragma warning restore WPF0001
        var xaml = XDocument.Load(Path.GetFullPath("src/OfframpShift.App/App.xaml"));
        var resources = xaml.Descendants().First(x => x.Name.LocalName == "ResourceDictionary");
        application.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        var result = 1; var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { result = await Exercise(secondary); }
            catch (Exception e) { Console.WriteLine("Lifecycle unavailable: " + e.GetType().Name); }
            finally { frame.Continue = false; }
        });
        Dispatcher.PushFrame(frame);
        return result;
    }

    private sealed class Platform : INudgePlatform
    {
        private readonly NudgePlatform native = new();
        internal long PresentationMs;
        internal string AfterShow = "NotAttempted";
        internal bool DpiChanged, ActivationAuthorized;
        public nint ForegroundWindow => native.ForegroundWindow;
        public string PlacementStatus => native.PlacementStatus;
        private string Foreground(NudgeWindow window, nint original) => ForegroundWindow == original ? "Original" :
            ForegroundWindow == new WindowInteropHelper(window).Handle ? "Candidate" : "ExternalOrUnavailable";
        public async Task<bool> PresentAsync(NudgeWindow window, nint foreground)
        {
            var started = Stopwatch.StartNew();
            var result = await native.PresentAsync(window, foreground); PresentationMs = started.ElapsedMilliseconds;
            AfterShow = Foreground(window, foreground); DpiChanged = native.DpiChangedDuringPlacement;
            return result;
        }
        public bool RaiseWithoutActivation(NudgeWindow window) => false; // Keep this diagnostic nonintrusive.
        // Diagnostic observes nonactivating Show and authorization only, never requests focus.
        public bool Activate(NudgeWindow window) { ActivationAuthorized = true; return false; }
    }

    private static async Task<int> Exercise(bool secondary)
    {
        var foreground = GetForegroundWindow();
        var screen = System.Windows.Forms.Screen.FromHandle(foreground);
        if (foreground == 0 || screen.Primary == secondary)
        { Console.WriteLine("TargetMonitorNotForeground: run with an active application on the requested monitor."); return 2; }
        var area = screen.WorkingArea;
        var root = Path.GetFullPath(Path.GetTempPath());
        var path = Path.Combine(root, "offramp-nudge-lifecycle-" + Guid.NewGuid().ToString("N"));
        MonitoringController? controller = null; NudgePresenter? presenter = null;
        try
        {
            var clock = new ObservationClock();
            EvidencePoint point;
            using (var source = new WindowsObservationSource(clock)) point = source.Read();
            if (point.Desktop.Current is not { } personal)
                throw new InvalidOperationException("Current desktop unavailable.");
            var work = point.Desktop.Desktops.FirstOrDefault(x => x != personal);
            if (work == Guid.Empty)
                throw new InvalidOperationException("Two observed desktops required.");
            Directory.CreateDirectory(path);
            var settings = new WindowsSettingsStore(path); settings.Load();
            settings.Save(new()
            {
                Consent = true,
                MonitoringEnabled = true,
                Build = WindowsObservationSource.Build,
                Personal = personal,
                Work = work,
                ReminderStyle = ReminderStyle.InteractiveWindow
            });
            using (var session = DurableMonitoringSession.Open(clock, new()
            {
                Accounting = AccountingMode.BestEffort,
                AllowanceMs = 1_000,
                RequiredBreakMs = 600_000,
                IdleThresholdMs = 600_000
            }, path)) { }
            controller = WindowsApplication.Start(path);
            var platform = new Platform(); var checks = new List<object>();
            presenter = new(async command =>
            {
                var started = Stopwatch.StartNew(); var result = await controller.ExecuteAsync(command);
                checks.Add(new { stage = command.Kind.ToString(), result.Succeeded, result.Message, ms = started.ElapsedMilliseconds });
                return result;
            }, () => controller.Latest, _ => { }, platform);
            var elapsed = Stopwatch.StartNew(); var consumed = false; long ttlAtConsumption = 0;
            while (elapsed.ElapsedMilliseconds < 15_000)
            {
                presenter.Refresh();
                if (controller.TryTakeReminder(out var reminder) && reminder is not null)
                {
                    consumed = true; ttlAtConsumption = reminder.ExpiresTick - Environment.TickCount64;
                    await presenter.PresentAsync(reminder);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    presenter.Refresh(); break;
                }
                await Task.Delay(50);
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                build = WindowsObservationSource.Build,
                monitor = secondary ? "Secondary" : "Primary",
                workWidth = area.Width,
                workHeight = area.Height,
                consumed,
                ttlAtConsumption,
                checks,
                platform.PresentationMs,
                platform.DpiChanged,
                platform.AfterShow,
                platform.ActivationAuthorized,
                focusRequested = false,
                windowDpi = presenter.Window is { } nudge ? System.Windows.Media.VisualTreeHelper.GetDpi(nudge).PixelsPerInchX : (double?)null,
                onForegroundMonitor = presenter.Window is { } shown && NudgePlatform.OnForegroundMonitor(new WindowInteropHelper(shown).Handle, foreground),
                status = platform.ActivationAuthorized ? "Presentation and strict confirmation completed; diagnostic skipped focus." : presenter.Status,
                visible = presenter.Window?.IsVisible == true,
                hostStatus = controller.Latest?.Status
            }));
            return presenter.Window?.IsVisible == true ? 0 : 1;
        }
        finally
        {
            presenter?.Close();
            if (controller is not null) await controller.StopAsync();
            // Verify the exclusively owned absolute directory before recursive cleanup.
            if (Path.GetDirectoryName(Path.GetFullPath(path)) == Path.TrimEndingDirectorySeparator(root) &&
                Path.GetFileName(path).StartsWith("offramp-nudge-lifecycle-", StringComparison.Ordinal) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
