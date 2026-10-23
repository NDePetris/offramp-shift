using System.IO;
using System.Runtime.InteropServices;
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
        var index = 0;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            Inspect(seed, Path.Combine(output, "monitor-" + ++index), screen);
    }
    private static void Inspect(HostSnapshot seed, string output, System.Windows.Forms.Screen screen)
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
        window.Apply(value); window.Show();
        var area = screen.WorkingArea;
        if (!SetWindowPos(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0, area.X + 40, area.Y + 40, 0, 0, 0x0015))
            throw new InvalidOperationException("Could not position the owned test window without activation.");
        Settle(window);
        if (Math.Abs(window.ActualWidth - 440) > 1 || Math.Abs(window.ActualHeight - 540) > 1) throw new InvalidOperationException("Native initial dimensions differ.");
        var dpi = VisualTreeHelper.GetDpi(window);
        File.WriteAllText(Path.Combine(output, "scroll-template.xaml"), System.Windows.Markup.XamlWriter.Save(window.HomePage.Template));
        File.WriteAllText(Path.Combine(output, "environment.txt"), $"OS: {Environment.OSVersion.Version}; x64 process: {Environment.Is64BitProcess}; DPI: {dpi.PixelsPerInchX} x {dpi.PixelsPerInchY}.\nSynthetic state in a shown WPF HWND; no monitoring or owner data.\n");
        foreach (var dark in new[] { false, true })
        {
#pragma warning disable WPF0001
            Application.Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            window.ApplyBrand(dark, false);
            foreach (var state in new[] { "normal", "subminute", "exhausted", "extension", "paused", "stale", "unavailable", "work", "latched", "pending" })
            {
                var next = value with { Sequence = ++valueSequence, PublishedTick = Environment.TickCount64 };
                next = state switch
                {
                    "exhausted" => next with { State = next.State! with { UsageMs = 2_040_000, Exhausted = true, Intervention = InterventionState.Nudging, ReminderRemainingMs = 60_000 } },
                    "subminute" => next with { State = next.State! with { UsageMs = next.Configuration!.AllowanceMs - 45_000 } },
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
                if ((state is "normal" or "subminute" or "exhausted" or "extension" or "paused") && window.HomePage.ScrollableHeight > 0.5)
                    throw new InvalidOperationException("Common healthy Home state should fit at the starting size.");
            }
            window.Model.SetCommandPending(false); window.ReportAction("");
            InspectReminder(output, area, dark);
            var diagnostics = new DiagnosticsWindow(window, new CommandCoordinator(), () => { }) { ShowActivated = false, ShowInTaskbar = false };
            diagnostics.Show();
            SetWindowPos(new System.Windows.Interop.WindowInteropHelper(diagnostics).Handle, 0, area.X + 40, area.Y + 40, 0, 0, 0x0015);
            Capture(diagnostics, Path.Combine(output, $"{(dark ? "dark" : "light")}-diagnostics.png"));
            diagnostics.Width = 640; diagnostics.Height = 480;
            Capture(diagnostics, Path.Combine(output, $"{(dark ? "dark" : "light")}-diagnostics-minimum.png"));
            diagnostics.Close();
            window.OpenSettings(); window.UpdateLayout(); Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-settings.png"));
            foreach (var button in ((Panel)window.SettingsOverview.Content).Children.OfType<Button>())
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                if (window.SettingsDetail.Visibility != Visibility.Visible) throw new InvalidOperationException("Native detail navigation failed.");
                Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-settings-{button.Tag}.png"));
                window.Width = 400; window.Height = 500;
                Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-settings-{button.Tag}-minimum.png"));
                window.SettingsDetail.ScrollToEnd(); Settle(window);
                if (window.SettingsDetail.ScrollableHeight > 0 && window.SettingsDetail.VerticalOffset <= 0) throw new InvalidOperationException("Settings must remain scrollable.");
                window.Width = 440; window.Height = 540;
                window.NavigateBack();
            }
            window.NavigateBack(); window.Apply(value with { Sequence = ++valueSequence, PublishedTick = Environment.TickCount64 });
            window.Width = 400; window.Height = 500; window.UpdateLayout();
            Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-minimum.png"));
            window.Apply(value with { Sequence = ++valueSequence, PublishedTick = Environment.TickCount64, State = value.State! with { UsageMs = long.MaxValue, Exhausted = true } });
            Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-long-timer.png"));
            window.ApplyBrand(dark, true);
            Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-system-brush-simulation.png"));
            window.ApplyBrand(dark, false);
            window.Width = 700; window.Height = 700; window.UpdateLayout();
            Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-wide.png"));
            window.Width = 440; window.Height = 540;
            window.Model.SetCommandPending(false);
            window.Apply(value with { Sequence = ++valueSequence, PublishedTick = Environment.TickCount64, Available = false, Detail = string.Join(" ", Enumerable.Repeat("Observations unavailable. Review monitoring after Windows settles; recorded values are retained.", 8)) });
            foreach (var factor in new[] { 1.5, 2.0 })
            {
                // Layout stress only, not a claim of changing/testing Windows' text setting.
                var texts = Descendants(window.HomePage).OfType<TextBlock>().Select(text => (Text: text, Size: text.FontSize)).ToArray();
                foreach (var text in texts) text.Text.FontSize = text.Size * factor;
                Capture(window, Path.Combine(output, $"{(dark ? "dark" : "light")}-enlarged-{factor}.png"));
                window.HomePage.ScrollToEnd(); Settle(window);
                if (window.HomePage.VerticalOffset <= 0) throw new InvalidOperationException("Enlarged long messages must scroll.");
                foreach (var text in texts) text.Text.FontSize = text.Size;
                window.HomePage.ScrollToHome(); Settle(window);
            }
            window.ReportAction("");
        }
        window.Model.SetCommandPending(false); window.Apply(value with { Sequence = ++valueSequence, PublishedTick = Environment.TickCount64 });
        using (var tray = new TrayService(() => { }, () => { }, _ => Task.FromResult(HostCommandResult.Success()), () => Task.CompletedTask))
        { tray.Apply(window.Model.Snapshot!, window.Model); tray.ApplyBrand(true); tray.ApplyBrand(false); }
        window.Hide();
        Console.WriteLine("PASS: native WPF initial size, resize, light/dark, routed settings navigation and transient tray create/update/dispose (synthetic state; no live monitoring, physical keyboard, Narrator or tray-menu interaction acceptance).");
    }
    private static void InspectReminder(string output, System.Drawing.Rectangle area, bool dark)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            var pending = new TaskCompletionSource<HostCommandResult>();
            var reminder = new NudgeWindow(_ => pending.Task, openMain: () => { })
            { ShowActivated = false, ShowInTaskbar = false };
            reminder.ApplyBrand(dark, false); reminder.SetSwitchAvailability("");
            reminder.Show();
            SetWindowPos(new System.Windows.Interop.WindowInteropHelper(reminder).Handle, 0, area.X + 40, area.Y + 40, 0, 0, 0x0015);
            var prefix = dark ? "dark" : "light";
            void Shot(string name) => Capture(reminder, Path.Combine(output, $"{prefix}-reminder{name}.png"));
            Shot("");
            if (reminder.ActionScroll.ScrollableHeight > 0.5) throw new InvalidOperationException("Default reminder actions must fit.");
            var bounds = (reminder.ActualWidth, reminder.ActualHeight, reminder.Left, reminder.Top);
            reminder.HelpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Shot("-help");
            if (bounds != (reminder.ActualWidth, reminder.ActualHeight, reminder.Left, reminder.Top)) throw new InvalidOperationException("Help resized/repositioned reminder.");
            reminder.HelpScroll.ScrollToEnd(); Settle(reminder);
            if (reminder.HelpScroll.VerticalOffset <= 0) throw new InvalidOperationException("Help must scroll within bounded geometry.");
            Shot("-help-end"); reminder.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            reminder.SetSwitchAvailability("Enable experimental switching in Settings to use Switch to Work."); Shot("-unavailable");
            reminder.SetSwitchAvailability("");
            var operation = reminder.Submit(300_000); Shot("-pending");
            reminder.HelpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pending.SetResult(HostCommandResult.Failure("Local storage is unavailable. Review recovery in Offramp Shift."));
            Settle(reminder); Shot("-error");
            if (!operation.IsCompleted || reminder.HelpView.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Failure must return to actions.");
            reminder.Width = 320; reminder.Height = 260; reminder.FontSize = 21;
            reminder.NudgeHeading.FontSize = 30;
            reminder.SetSwitchAvailability("Switching unavailable. Select Work manually with Win+Tab.");
            Shot("-constrained"); reminder.ActionScroll.ScrollToEnd(); Settle(reminder);
            if (reminder.ActionScroll.VerticalOffset <= 0) throw new InvalidOperationException("Enlarged reminder errors/actions must scroll.");
            Shot("-constrained-end");
            reminder.HelpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Shot("-constrained-help");
            reminder.CloseAutomatically();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static long valueSequence = 100;
    private static void Settle(Window window)
    {
        // Pump native resize/DPI messages as well as WPF layout before measuring.
        var frame = new DispatcherFrame();
        window.UpdateLayout();
        // Allow an actual compositor frame after layout/navigation/DPI changes;
        // reaching ApplicationIdle once can still print the previous page.
        var settle = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(120) };
        settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; };
        settle.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(Window window, string path)
    {
        Settle(window);
        // Settle the final extent after a sequence of bound content changes.
        foreach (var viewer in Descendants(window).OfType<ScrollViewer>().Where(x => x.IsVisible)) viewer.InvalidateMeasure();
        Settle(window);
        foreach (var viewer in (window is MainWindow main ? new[] { main.HomePage, main.SettingsOverview, main.SettingsDetail } : Descendants(window).OfType<ScrollViewer>()).Where(x => x.IsVisible))
        {
            var facts = Descendants(viewer).OfType<FrameworkElement>().Where(x => x is ScrollContentPresenter or System.Windows.Controls.Primitives.ScrollBar).Select(x =>
                $"{x.GetType().Name} {x.Name}: {x.TransformToAncestor(viewer).TransformBounds(new Rect(x.RenderSize))}; visibility={x.Visibility}");
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "layout.txt"), $"{Path.GetFileName(path)} {viewer.Name}: viewport={viewer.ViewportWidth}x{viewer.ViewportHeight}; extent={viewer.ExtentWidth}x{viewer.ExtentHeight}\n{string.Join("\n", facts)}\n");
            ScrollLayoutChecks.Clearance(viewer);
        }
        // Capture only this harness-owned HWND, including native chrome. A
        // VisualBrush of the margined root can shift/crop the right/bottom edge.
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("Owned window bounds unavailable.");
        using var bitmap = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var context = graphics.GetHdc();
            try { if (!PrintWindow(handle, context, 0)) throw new InvalidOperationException("Owned window capture unavailable."); }
            finally { graphics.ReleaseHdc(context); }
        }
        var renderedSamples = 0;
        for (var y = 70; y < bitmap.Height - 30; y += 16)
            for (var x = 30; x < bitmap.Width - 30; x += 16)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.ToArgb() != System.Drawing.Color.White.ToArgb() && color.ToArgb() != System.Drawing.Color.Black.ToArgb()) renderedSamples++;
            }
        if (renderedSamples < 20) throw new InvalidOperationException("Native print capture contains no rendered WPF content.");
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowBounds bounds);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint context, uint flags);
}
