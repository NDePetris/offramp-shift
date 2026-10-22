using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;

// Explicit opt-in native WPF shell inspection. Synthetic facts, no host,
// user storage, providers or switching. Not live accounting/accessibility proof.
internal static class HomeShellInspection
{
    internal static void Run(HostSnapshot seed, string output)
    {
        Directory.CreateDirectory(output);
        var window = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask)
        { ShowActivated = false, ShowInTaskbar = false };
        var value = seed with
        {
            Sequence = 100,
            AccountingPresentationEpoch = Guid.NewGuid(),
            State = seed.State! with { UsageMs = 720_000, EstimatedUsageMs = 720_000, BreakProgressMs = 0 },
            Configuration = seed.Configuration! with { RequiredBreakMs = 300_000 },
            Settings = seed.Settings with { ReminderStyle = ReminderStyle.InteractiveWindow }
        };
        window.Apply(value); window.Show(); Settle(window);
        if (Math.Abs(window.ActualWidth - 440) > 1 || Math.Abs(window.ActualHeight - 540) > 1) throw new InvalidOperationException("Native initial dimensions differ.");
        var dpi = VisualTreeHelper.GetDpi(window);
        File.WriteAllText(Path.Combine(output, "environment.txt"), $"OS: {Environment.OSVersion.Version}; x64 process: {Environment.Is64BitProcess}; DPI: {dpi.PixelsPerInchX} x {dpi.PixelsPerInchY}.\nSynthetic state in a shown WPF HWND; no monitoring or owner data.\n");
        foreach (var dark in new[] { false, true })
        {
#pragma warning disable WPF0001
            Application.Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            window.ApplyBrand(dark, false);
            foreach (var state in new[] { "normal", "exhausted", "extension", "paused", "stale", "unavailable", "work", "latched", "pending" })
            {
                var next = value with { Sequence = ++valueSequence, PublishedTick = Environment.TickCount64 };
                next = state switch
                {
                    "exhausted" => next with { State = next.State! with { UsageMs = 2_040_000, Exhausted = true, Intervention = InterventionState.Nudging, ReminderRemainingMs = 60_000 } },
                    "extension" => next with { State = next.State! with { UsageMs = 2_040_000, Exhausted = true, Intervention = InterventionState.PersonalDeferred, SnoozeRemainingMs = 120_000 } },
                    "paused" => next with { State = next.State! with { Paused = true } },
                    "stale" => next with { PublishedTick = Environment.TickCount64 - 2_001 },
                    "unavailable" => next with { Available = false, Detail = "Foreground observations unavailable. Unknown time earns no credit." },
                    "work" => next with { Context = DesktopScope.Work, State = next.State! with { BreakProgressMs = 120_000 } },
                    "latched" => next with { State = next.State! with { Exhausted = true } },
                    _ => next
                };
                window.Apply(next); window.Model.SetCommandPending(state == "pending");
                window.ReportAction(state == "pending" ? "Pause requested · Waiting for confirmation…" : "");
                window.UpdateLayout(); Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-{state}.png"));
            }
            window.OpenSettings(); window.UpdateLayout(); Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-settings.png"));
            foreach (var button in ((Panel)window.SettingsOverview.Content).Children.OfType<Button>())
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                if (window.SettingsDetail.Visibility != Visibility.Visible) throw new InvalidOperationException("Native detail navigation failed.");
                window.NavigateBack();
            }
            window.NavigateBack(); window.Apply(value with { Sequence = ++valueSequence, PublishedTick = Environment.TickCount64 });
            window.Width = 400; window.Height = 500; window.UpdateLayout();
            Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-minimum.png"));
            window.Width = 700; window.Height = 700; window.UpdateLayout();
            Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-wide.png"));
            window.Width = 440; window.Height = 540;
        }
        window.Model.SetCommandPending(false); window.Apply(value with { Sequence = ++valueSequence, PublishedTick = Environment.TickCount64 });
        using (var tray = new TrayService(() => { }, () => { }, _ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask))
        { tray.Apply(window.Model.Snapshot!, window.Model); tray.ApplyBrand(true); tray.ApplyBrand(false); }
        window.Hide();
        Console.WriteLine("PASS: native WPF initial size, resize, light/dark, routed settings navigation and transient tray create/update/dispose (synthetic state; no live monitoring, physical keyboard, Narrator or tray-menu interaction acceptance).");
    }
    private static long valueSequence = 100;
    private static void Settle(MainWindow window)
    {
        // Pump native resize/DPI messages as well as WPF layout before measuring.
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }
    private static void Capture(MainWindow window, string path)
    {
        Settle(window);
        var content = (FrameworkElement)window.Content; var dpi = VisualTreeHelper.GetDpi(content);
        var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new(0, 0, width, height));
            var brush = new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new(0, 0, content.ActualWidth, content.ActualHeight), Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            context.DrawRectangle(brush, null, new(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
