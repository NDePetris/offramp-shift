using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
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
                dialog.CustomChoice.IsChecked = custom;
                if (custom)
                {
                    dialog.Date.SelectedDate = new DateTime(2026, 11, 1); dialog.Hour.SelectedIndex = 0; dialog.Minute.SelectedIndex = 30; dialog.Period.SelectedIndex = 0;
                    dialog.Validation.Text = "This clock time occurs twice. Choose the first or second occurrence.";
                }
                Capture(dialog, $"{(dark ? "dark" : "light")}-{(custom ? "custom" : "presets")}", 440, custom ? 580 : 330);
                if (custom)
                {
                    Capture(dialog, $"{(dark ? "dark" : "light")}-custom-minimum", 400, 320);
                    dialog.PauseScroll.ScrollToEnd(); dialog.UpdateLayout();
                    Capture(dialog, $"{(dark ? "dark" : "light")}-custom-minimum-end", 400, 320);
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
        void Check(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        { if (!condition) throw new InvalidOperationException($"Timed Pause assertion failed at line {line}."); }
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
        test("Pause choices are exclusive and require confirmation", () =>
        {
            var main = Main();
            var clock = now;
            var dialog = new PauseDialog(main, () => clock, TimeZoneInfo.Utc);
            RadioButton[] choices = [dialog.FifteenMinutes, dialog.ThirtyMinutes, dialog.OneHour, dialog.TwoHours, dialog.CustomChoice, dialog.ManualChoice];
            Check(choices.Count(choice => choice.IsChecked == true) == 1 && dialog.FifteenMinutes.IsChecked == true);
            foreach (var choice in choices.Reverse().Concat(choices))
            {
                choice.IsChecked = true;
                Check(choices.Count(option => option.IsChecked == true) == 1 && choice.IsChecked == true);
                Check(dialog.Selection is null);
                Check(dialog.Custom.Visibility == (choice == dialog.CustomChoice ? Visibility.Visible : Visibility.Collapsed));
            }
            dialog.ThirtyMinutes.IsChecked = true; clock = now.AddMinutes(2);
            Check(dialog.TryConfirmSelection());
            Check(dialog.Selection?.PauseUntilUtc == clock.AddMinutes(30));
            dialog.Close();
            var canceled = new PauseDialog(main, () => now, TimeZoneInfo.Utc);
            canceled.CustomChoice.IsChecked = true; canceled.Minute.SelectedIndex = 17; canceled.Close();
            Check(canceled.Selection is null); main.Close();
        });
        test("custom selectors keep all minutes and correctly convert midnight and noon", () =>
        {
            var main = Main();
            foreach (var (hour, period, expectedHour) in new[] { (12, 0, 0), (12, 1, 12), (1, 1, 13), (11, 0, 11) })
            {
                var dialog = new PauseDialog(main, () => now, TimeZoneInfo.Utc);
                dialog.CustomChoice.IsChecked = true; dialog.Date.SelectedDate = now.Date.AddDays(1);
                dialog.Hour.SelectedIndex = hour - 1; dialog.Minute.SelectedIndex = 17; dialog.Period.SelectedIndex = period;
                Check(dialog.Hour.Items.Count == 12 && dialog.Minute.Items.Count == 60 && dialog.Period.Items.Count == 2);
                Check(!dialog.Hour.IsEditable && !dialog.Minute.IsEditable && !dialog.Period.IsEditable);
                Check(dialog.Minute.Items[0].ToString() == "00" && dialog.Minute.Items[59].ToString() == "59");
                Check(dialog.TryConfirmSelection());
                Check(dialog.Selection?.PauseUntilUtc == new DateTimeOffset(now.Date.AddDays(1).AddHours(expectedHour).AddMinutes(17), TimeSpan.Zero));
                dialog.Close();
            }
            var manual = new PauseDialog(main, () => now, TimeZoneInfo.Utc);
            manual.ManualChoice.IsChecked = true;
            Check(manual.TryConfirmSelection() && manual.Selection is { Kind: HostCommandKind.Pause, PauseUntilUtc: null });
            manual.Close(); main.Close();
        });
        test("custom default is the next future quarter-hour including date and DST rollover", () =>
        {
            foreach (var (instant, expected) in new[] {
                (now, now.Date.AddHours(12.25)), (now.AddMinutes(1), now.Date.AddHours(12.25)),
                (now.AddMinutes(15), now.Date.AddHours(12.5)),
                (new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero), new DateTime(2027, 1, 1)) })
                Check(PauseSelection.NextQuarterHour(instant, TimeZoneInfo.Utc) == expected);
            var central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            Check(PauseSelection.NextQuarterHour(new(2026, 3, 8, 7, 59, 0, TimeSpan.Zero), central) == new DateTime(2026, 3, 8, 3, 0, 0));
            Check(PauseSelection.NextQuarterHour(new(2026, 11, 1, 6, 59, 0, TimeSpan.Zero), central) == new DateTime(2026, 11, 1, 1, 0, 0));
            var main = Main(); var dialog = new PauseDialog(main, () => new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero), TimeZoneInfo.Utc);
            dialog.CustomChoice.IsChecked = true;
            Check(dialog.Date.SelectedDate == new DateTime(2027, 1, 1) && dialog.Hour.SelectedIndex == 11 && dialog.Minute.SelectedIndex == 0 && dialog.Period.SelectedIndex == 0);
            Check(dialog.TryConfirmSelection() && dialog.Selection?.PauseUntilUtc == new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Check(PauseSelection.ZoneLabel(central) == "Local time \u00b7 Central Time");
            Check(!PauseSelection.ZoneLabel(central).Contains("UTC", StringComparison.Ordinal));
            dialog.Close(); main.Close();
        });
        test("custom selector validation rejects gaps and past times and requires a DST occurrence", () =>
        {
            var main = Main(); var central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            var dialog = new PauseDialog(main, () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), central);
            dialog.CustomChoice.IsChecked = true;
            dialog.Date.SelectedDate = new DateTime(2026, 3, 8); dialog.Hour.SelectedIndex = 1; dialog.Minute.SelectedIndex = 30; dialog.Period.SelectedIndex = 0;
            Check(!dialog.TryConfirmSelection() && dialog.Selection is null && dialog.Validation.Text.Contains("does not exist", StringComparison.Ordinal));
            dialog.Date.SelectedDate = new DateTime(2026, 11, 1); dialog.Hour.SelectedIndex = 0;
            Check(dialog.Ambiguity.Visibility == Visibility.Visible && dialog.Occurrence.Items.Count == 2 && dialog.Occurrence.SelectedIndex == -1);
            Check(!dialog.TryConfirmSelection() && dialog.Selection is null);
            dialog.Occurrence.SelectedIndex = 0;
            Check(dialog.TryConfirmSelection() && dialog.Selection?.PauseUntilUtc == new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero));
            dialog.Close();
            var past = new PauseDialog(main, () => now, TimeZoneInfo.Utc);
            past.CustomChoice.IsChecked = true; past.Date.SelectedDate = now.Date.AddDays(-1);
            Check(!past.TryConfirmSelection() && past.Selection is null && past.Validation.Text.Contains("future", StringComparison.Ordinal));
            past.Close(); main.Close();
        });
        test("Pause controls preserve accessible names focus conventions and compact expanded layouts", () =>
        {
            var main = Main(); var dialog = new PauseDialog(main, () => now, TimeZoneInfo.Utc);
            Check(dialog.SizeToContent == SizeToContent.Height && dialog.MinWidth >= 400 && dialog.MinHeight >= 300);
            Check(dialog.PauseButton.IsDefault && dialog.CancelButton.IsCancel);
            Check(AutomationProperties.GetName(dialog.Date) == "Resume date");
            foreach (var control in new[] { dialog.Hour, dialog.Minute, dialog.Period })
                Check(control.Focusable && control.IsTabStop && AutomationProperties.GetName(control).StartsWith("Resume", StringComparison.Ordinal));
            Check(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(dialog.FifteenMinutes)?.GetAutomationControlType() == AutomationControlType.RadioButton);
            var content = (FrameworkElement)dialog.Content;
            content.Measure(new(440, double.PositiveInfinity)); var compact = content.DesiredSize.Height;
            dialog.CustomChoice.IsChecked = true;
            content.UpdateLayout(); content.InvalidateMeasure(); content.Measure(new(440, double.PositiveInfinity));
            if (content.DesiredSize.Height <= compact + 100)
                throw new InvalidOperationException($"Expected custom expansion: compact={compact}, custom={content.DesiredSize.Height}, fields={dialog.Custom.DesiredSize.Height}.");
            content.Measure(new(400, 320)); content.Arrange(new(0, 0, 400, 320)); content.UpdateLayout();
            Check(dialog.QuickDurations.ActualWidth > 240 && dialog.PauseScroll.ScrollableHeight > 0);
            Check(dialog.PauseButton.ActualWidth > 0 && dialog.CancelButton.ActualWidth > 0);
            dialog.ManualChoice.IsChecked = true;
            content.Measure(new(440, double.PositiveInfinity)); Check(Math.Abs(content.DesiredSize.Height - compact) < 1);
            dialog.Close(); main.Close();
        });
        MainWindow Main() => new(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
    }
}
