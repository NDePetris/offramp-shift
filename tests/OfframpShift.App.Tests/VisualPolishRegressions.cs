using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using OfframpShift.App;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;

internal static class VisualPolishRegressions
{
    internal static void Run(Action<string, Action> test, Func<HostSnapshot> snapshot)
    {
        void Check(bool value) { if (!value) throw new InvalidOperationException("Visual polish assertion failed."); }
        test("Home scene fills and clips the existing hero without owning its height or reminder composition", () =>
        {
            var main = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            main.Apply(snapshot()); main.ApplyBrand(false, false);
            var root = (FrameworkElement)main.Content;
            foreach (var width in new[] { 320d, 360, 480 })
            {
                root.Measure(new(width, 440)); root.Arrange(new(0, 0, width, 440)); root.UpdateLayout();
                Check(main.HeroRoad.HomeBackground && main.HeroRoad.ActualWidth > 240);
                Check(Math.Abs(main.HeroRoad.ActualWidth - main.TimerHero.ActualWidth) < 0.5);
                Check(main.HeroRoad.ActualHeight < 200 && Math.Abs(main.HeroRoad.ActualHeight - main.TimerHero.ActualHeight) < 0.5);
                Check(main.HeroRoad.Clip is RectangleGeometry { IsFrozen: true, RadiusX: 20, RadiusY: 20 } clip && clip.Rect.Size == main.HeroRoad.RenderSize);
                Check(ReferenceEquals(main.HeroTimer.Background, main.Resources["ShiftHomeHero"]) && main.HeroRoad.OpacityMask is null);
            }
            var reminder = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()));
            Check(!HomeShellInspection.Descendants(reminder).OfType<QuietRoad>().Any());
            reminder.CloseAutomatically();
        });
        test("scenic hero retains authoritative labels values and qualifiers across operational states", () =>
        {
            var main = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            var seed = snapshot();
            foreach (var state in new[] { "normal", "exhausted", "extension", "paused", "stale", "unavailable", "latched", "long" })
            {
                var value = seed with { Settings = seed.Settings with { ReminderStyle = ReminderStyle.InteractiveWindow } };
                value = state switch
                {
                    "exhausted" => value with { State = value.State! with { UsageMs = value.Configuration!.AllowanceMs + 1, Exhausted = true, Intervention = InterventionState.Nudging, ReminderRemainingMs = 9_001 } },
                    "extension" => value with { State = value.State! with { Exhausted = true, Intervention = InterventionState.PersonalDeferred, SnoozeRemainingMs = 45_001 } },
                    "paused" => value with { State = value.State! with { Paused = true } },
                    "stale" => value with { PublishedTick = Environment.TickCount64 - 2_001 },
                    "unavailable" => value with { Available = false },
                    "latched" => value with { State = value.State! with { Exhausted = true } },
                    "long" => value with { State = value.State! with { UsageMs = long.MaxValue, Exhausted = true } },
                    _ => value
                };
                main.Apply(value);
                var root = (FrameworkElement)main.Content;
                root.Measure(new(360, 400)); root.Arrange(new(0, 0, 360, 400)); root.UpdateLayout();
                var texts = HomeShellInspection.Descendants(main.TimerHero).OfType<TextBlock>().ToArray();
                Check(main.HeroTimer.Text == main.Model.HeroValue && texts.Any(x => x.Text == main.Model.HeroLabel));
                Check(texts.Any(x => x.Text == main.Model.Arithmetic));
                if (main.Model.RecordedQualifier.Length > 0) Check(texts.Any(x => x.Visibility == Visibility.Visible && x.Text == main.Model.RecordedQualifier));
                if (main.Model.ShowReminder) Check(texts.Any(x => x.Text == main.Model.ReminderValue));
            }
        });
        test("Home and reminder share paper sage and coordinated contrast-safe theme roles", () =>
        {
            var main = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            var reminder = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()));
            foreach (var dark in new[] { false, true })
                foreach (var contrast in new[] { false, true })
                {
                    main.ApplyBrand(dark, contrast); reminder.ApplyBrand(dark, contrast);
                    foreach (var role in new[] { "ShiftCanvas", "ShiftText", "ShiftHero", "ShiftSupporting", "ShiftAccent" })
                        Check(((SolidColorBrush)main.Resources[role]).Color == ((SolidColorBrush)reminder.Resources[role]).Color);
                    Check(main.HeroRoad.Visibility == (contrast ? Visibility.Collapsed : Visibility.Visible));
                    Check(reminder.FamilySymbol.Visibility == (contrast ? Visibility.Collapsed : Visibility.Visible));
                    Check(!main.HeroRoad.Focusable && !main.HeroRoad.IsHitTestVisible && UIElementAutomationPeer.CreatePeerForElement(main.HeroRoad) is null);
                    if (contrast) Check(ReferenceEquals(main.Resources["ShiftHero"], SystemColors.WindowBrush));
                    else
                    {
                        Check(((SolidColorBrush)main.Resources["ShiftCanvas"]).Color == (Color)ColorConverter.ConvertFromString(dark ? "#1F2B28" : "#F6F3EC"));
                        foreach (var surface in new[] { "ShiftCanvas", "ShiftHero", "ShiftHomeHero" })
                            foreach (var text in new[] { "ShiftText", "ShiftSupporting", "ShiftAccent" })
                                Check(Contrast(((SolidColorBrush)main.Resources[surface]).Color, ((SolidColorBrush)main.Resources[text]).Color) >= 4.5);
                    }
                }
            reminder.CloseAutomatically();
        });
        test("reminder navigation keeps the authorized surface and never submits time or switching", () =>
        {
            var opens = 0; var deferrals = 0; var switches = 0;
            var main = new MainWindow(_ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask, () => Task.CompletedTask);
            main.Apply(snapshot()); var recorded = main.Model.Recorded;
            var reminder = new NudgeWindow(_ => { deferrals++; return Task.FromResult(HostCommandResult.Success()); },
                () => { switches++; return Task.FromResult(DesktopFeasibility.SwitchCompletion.Reject("Unavailable")); }, () => opens++);
            new System.Windows.Interop.WindowInteropHelper(reminder).EnsureHandle();
            reminder.SetSwitchAvailability("Not consented. Use Win+Tab to select Work.");
            reminder.OpenMainButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            reminder.OpenMainButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(opens == 2 && deferrals == 0 && switches == 0 && reminder.CanReportFailure);
            Check(main.Model.Recorded == recorded && main.CurrentPage == "Home");
            Check(reminder.SwitchWork.Visibility == Visibility.Collapsed && !reminder.SwitchWork.IsEnabled);
            reminder.SetSwitchAvailability("");
            Check(reminder.SwitchWork.Visibility == Visibility.Visible && reminder.SwitchWork.IsEnabled);
            Check(reminder.SwitchWarning.Text.Length == 0 && reminder.SwitchAvailability.Text.Contains("experimental"));
            reminder.CloseAutomatically();
            reminder.OpenMainButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(opens == 2 && deferrals == 0 && switches == 0);
        });
        test("opening main while extra time is pending preserves pending controls and exact submission", () =>
        {
            var opens = 0; var calls = 0;
            var completion = new TaskCompletionSource<HostCommandResult>();
            var reminder = new NudgeWindow(_ => { calls++; return completion.Task; }, openMain: () => opens++);
            var pending = reminder.Submit(300_000);
            reminder.OpenMainButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(opens == 1 && calls == 1 && !reminder.OneMinute.IsEnabled && !reminder.FiveMinutes.IsEnabled && !pending.IsCompleted);
            reminder.CloseAutomatically(); completion.SetResult(HostCommandResult.Success());
            pending.GetAwaiter().GetResult();
            Check(calls == 1);
        });
    }
    private static double Contrast(Color first, Color second)
    {
        double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
