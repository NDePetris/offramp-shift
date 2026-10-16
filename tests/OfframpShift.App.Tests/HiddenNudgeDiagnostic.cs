using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using WindowsObservation;

internal static class HiddenNudgeDiagnostic
{
    // Explicit local exercise only. Never activate, mutate desktops, open
    // accounting/settings, or print desktop/foreground/window identities.
    internal static int Run(bool registration)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = $"{key?.GetValue("CurrentBuild")}.{key?.GetValue("UBR")}";
        if (!DesktopObservationPolicy.IsSupported(build, RuntimeInformation.ProcessArchitecture.ToString()))
        { Console.WriteLine("Hidden nudge diagnostic: unsupported build/architecture; no worker started."); return 2; }
        var window = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()));
        Process? worker = null;
        try
        {
            // Exercise the production cold path: no diagnostic-only layout warmup.
            var foreground = GetForegroundWindow();
            var platform = new NudgePlatform();
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var start = new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "OfframpShift.App.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--desktop-worker");
            worker = Process.Start(start) ?? throw new InvalidOperationException();
            worker.StandardInput.WriteLine("read"); worker.StandardInput.Flush();
            var line = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var reading = JsonSerializer.Deserialize<DesktopReading>(line ?? "null");
            if (reading is not { Health: Capability.Available, Current: { } current }) throw new InvalidOperationException();
            worker.StandardInput.WriteLine(FormattableString.Invariant($"nudge {hwnd.ToInt64()} {foreground.ToInt64()} {current:D}"));
            worker.StandardInput.Flush();
            var membership = Category(worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult());
            string? prepared = null, afterShow = null;
            var placed = false;
            var placementStage = "NotAttempted";
            if (registration)
            {
                worker.StandardInput.WriteLine(FormattableString.Invariant($"prepare-nudge {hwnd.ToInt64()} {foreground.ToInt64()} {current:D}"));
                worker.StandardInput.Flush();
                prepared = Category(worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult());
                if (prepared == nameof(NudgeDesktopCheck.Allowed))
                {
                    placed = platform.Place(window, foreground);
                    placementStage = platform.PlacementStatus;
                    if (placed)
                    {
                        platform.Show(window); // ShowActivated=false; never call Activate.
                        worker.StandardInput.WriteLine(FormattableString.Invariant($"nudge {hwnd.ToInt64()} {foreground.ToInt64()} {current:D}"));
                        worker.StandardInput.Flush();
                        afterShow = Category(worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult());
                    }
                }
            }
            worker.StandardInput.WriteLine("read"); worker.StandardInput.Flush();
            var finalLine = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var final = JsonSerializer.Deserialize<DesktopReading>(finalLine ?? "null");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                build,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                hidden = !window.IsVisible,
                placementStage,
                dpiChangedDuringPlacement = platform.DpiChangedDuringPlacement,
                windowDpi = System.Windows.Media.VisualTreeHelper.GetDpi(window).PixelsPerInchX,
                onForegroundMonitor = NudgePlatform.OnForegroundMonitor(hwnd, foreground),
                candidateIsForeground = GetForegroundWindow() == hwnd,
                foregroundUnavailable = GetForegroundWindow() == 0,
                foregroundStable = GetForegroundWindow() == foreground,
                desktopStable = final?.Current == current,
                membership,
                prepared,
                placed,
                afterShow
            }));
            return !registration || placed && afterShow == nameof(NudgeDesktopCheck.Allowed) &&
                GetForegroundWindow() == foreground && final?.Current == current &&
                NudgePlatform.OnForegroundMonitor(hwnd, foreground) ? 0 : 1;
        }
        catch { Console.WriteLine("Hidden nudge diagnostic: unavailable; no presentation attempted."); return 1; }
        finally
        {
            window.CloseAutomatically();
            Application.Current.Shutdown();
            if (worker is not null)
            {
                try { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(1_000); } }
                finally { worker.Dispose(); }
            }
        }
    }
    private static string Category(string? value) => Enum.TryParse<NudgeDesktopCheck>(value, out var parsed) && Enum.IsDefined(parsed) ? parsed.ToString() : "Unavailable";
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
