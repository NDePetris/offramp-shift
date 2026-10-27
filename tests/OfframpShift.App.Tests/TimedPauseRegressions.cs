using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;

internal static class TimedPauseRegressions
{
    // Opt-in offscreen rendering of synthetic UI only: no HWND activation,
    // observation providers, user storage, or live monitoring validation.
    internal static void Render(string output, HostSnapshot snapshot)
    {
        Directory.CreateDirectory(output);
        var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        foreach (var dark in new[] { false, true })
        {
#pragma warning disable WPF0001
            Application.Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            var main = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            main.ApplyBrand(dark, false);
            foreach (var custom in new[] { false, true })
            {
                var dialog = new PauseDialog(main, () => now, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"));
                dialog.Choice.SelectedIndex = custom ? 4 : 0;
                if (custom)
                {
                    dialog.Date.SelectedDate = new DateTime(2026, 11, 1); dialog.Time.Text = "01:30";
                    dialog.Validation.Text = "This clock time occurs twice. Choose the first or second occurrence.";
                }
                Capture(dialog, $"{(dark ? "dark" : "light")}-{(custom ? "custom" : "presets")}", 440, custom ? 600 : 500);
                if (custom)
                {
                    Capture(dialog, $"{(dark ? "dark" : "light")}-custom-minimum", 400, 400);
                    dialog.PauseScroll.ScrollToEnd(); dialog.UpdateLayout();
                    Capture(dialog, $"{(dark ? "dark" : "light")}-custom-minimum-end", 400, 400);
                }
                dialog.Close();
            }
            main.Apply(snapshot with { State = snapshot.State! with { Paused = true, PauseUntilUtc = DateTimeOffset.UtcNow.AddHours(1) } });
            Capture(main, $"{(dark ? "dark" : "light")}-home", 440, 540);
            main.Close();
        }
        void Capture(Window window, string name, int width, int height)
        {
            var content = (FrameworkElement)window.Content;
            content.Measure(new(width, height)); content.Arrange(new(0, 0, width, height)); content.UpdateLayout();
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing); bitmap.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file);
        }
    }

    internal static void Run(Action<string, Action> test, Func<HostSnapshot> seed)
    {
        var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        void Check(bool condition) { if (!condition) throw new InvalidOperationException("Timed Pause assertion failed."); }
        void Invalid(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid time accepted."); }
        test("pause presets and custom dates produce one UTC instant", () =>
        {
            foreach (var (index, minutes) in new[] { (0, 15), (1, 30), (2, 60), (3, 120) })
                Check(PauseSelection.Deadline(index, null, "", TimeZoneInfo.Utc, null, now) == now.AddMinutes(minutes));
            Check(PauseSelection.Deadline(5, null, "", TimeZoneInfo.Utc, null, now) is null);
            foreach (var days in new[] { 0, 1, 30 })
                Check(PauseSelection.Deadline(4, now.Date.AddDays(days), new DateTime(2026, 1, 1, 13, 30, 0).ToString("t"), TimeZoneInfo.Utc, null, now) == new DateTimeOffset(now.Date.AddDays(days).AddHours(13.5), TimeSpan.Zero));
            Invalid(() => PauseSelection.Deadline(4, now.Date, "11:00", TimeZoneInfo.Utc, null, now));
            Invalid(() => PauseSelection.Deadline(4, null, "13:00", TimeZoneInfo.Utc, null, now));
            Invalid(() => PauseSelection.Deadline(4, now.Date, "tomorrow", TimeZoneInfo.Utc, null, now));
        });
        test("DST gap is rejected and repeated local times require an explicit occurrence", () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            var before = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Invalid(() => PauseSelection.Deadline(4, new(2026, 3, 8), "02:30", zone, null, before));
            Invalid(() => PauseSelection.Deadline(4, new(2026, 11, 1), "01:30", zone, null, before));
            var first = PauseSelection.Deadline(4, new(2026, 11, 1), "01:30", zone, TimeSpan.FromHours(-5), before);
            var second = PauseSelection.Deadline(4, new(2026, 11, 1), "01:30", zone, TimeSpan.FromHours(-6), before);
            Check(first == new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero)); Check(second - first == TimeSpan.FromHours(1));
        });
        test("pause status formats local date without a countdown in both cultures", () =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                foreach (var culture in new[] { "en-US", "en-GB" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    Check(PauseSelection.Status(null, now, TimeZoneInfo.Utc) == "Paused until resumed");
                    Check(PauseSelection.Status(now.AddHours(1), now, TimeZoneInfo.Utc) == "Paused until " + now.AddHours(1).ToString("t"));
                    Check(PauseSelection.Status(now.AddDays(1), now, TimeZoneInfo.Utc).Contains("28", StringComparison.Ordinal));
                    Check(PauseSelection.Status(now.AddYears(1), now, TimeZoneInfo.Utc).Contains("2027", StringComparison.Ordinal));
                }
            }
            finally { CultureInfo.CurrentCulture = original; }
        });
        test("Home and tray agree and unavailable observation still allows Resume now", () =>
        {
            var snapshot = seed(); var model = new DashboardModel();
            snapshot = snapshot with { Available = false, State = snapshot.State! with { Paused = true, PauseUntilUtc = now.AddDays(1) } };
            model.Apply(snapshot, snapshot.PublishedTick);
            Check(model.CanResume && model.CanChangePause && model.MonitoringActionLabel == "Resume now");
            Check(model.PauseStatus == model.Tray.Status);
            var pending = snapshot with
            {
                Sequence = 2,
                Persistence = PersistenceStatus.PendingCommit,
                State = null,
                Configuration = null,
                PauseRecoveryDetail = "Pause end reached; ending Pause is not confirmed. Select Retry save."
            };
            model.Apply(pending, pending.PublishedTick);
            Check(model.PauseStatus == pending.PauseRecoveryDetail && model.Tray.Status == model.PauseStatus);
            Check(!model.CanResume && !model.CanChangePause && model.CanRetrySave);
        });
        test("shared Pause dialog cancels without a command and labels date/time controls", () =>
        {
            var main = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            var dialog = new PauseDialog(main, () => now, TimeZoneInfo.Utc);
            Check(dialog.Selection is null && dialog.Choice.Items.Count == 6);
            dialog.Choice.SelectedIndex = 4;
            Check(dialog.Custom.Visibility == Visibility.Visible);
            Check(AutomationProperties.GetName(dialog.Date) == "Resume date");
            Check(AutomationProperties.GetName(dialog.Time) == "Resume local clock time");
            dialog.Close(); Check(dialog.Selection is null); main.Close();
        });
    }
}
