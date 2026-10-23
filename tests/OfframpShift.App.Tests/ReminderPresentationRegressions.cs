using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OfframpShift.App;
using OfframpShift.ApplicationHost;

internal static class ReminderPresentationRegressions
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
    internal static void Run(Action<string, Action> test)
    {
        void Check(bool value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!value) throw new InvalidOperationException($"Reminder presentation assertion failed at {line}."); }
        test("minimal reminder has no scenic panel expander or ordinary default paragraphs", () =>
        {
            var window = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()));
            window.SetSwitchAvailability("");
            var root = (FrameworkElement)window.Content;
            root.Measure(new(384, 264)); root.Arrange(new(0, 0, 384, 264)); root.UpdateLayout();
            Check(window.Height == 300 && window.Width == 400 && window.SizeToContent == SizeToContent.Manual);
            Check(window.HelpView.Visibility == Visibility.Collapsed && window.SwitchWarning.Visibility == Visibility.Collapsed && window.Result.Visibility == Visibility.Collapsed);
            Check(!HomeShellInspection.Descendants(root).Any(x => x is QuietRoad or Expander));
            Check(window.SwitchWork.Visibility == Visibility.Visible && window.SwitchWork.IsEnabled && !window.SwitchWork.IsDefault);
            Check(window.ActionScroll.ScrollableHeight <= 0.5);
            foreach (var width in new[] { 384d, 304d })
            {
                root.Measure(new(width, 264)); root.Arrange(new(0, 0, width, 264)); root.UpdateLayout();
                var left = window.HelpButton.TransformToAncestor(window.FooterNavigation).TransformBounds(new Rect(window.HelpButton.RenderSize));
                var right = window.OpenMainButton.TransformToAncestor(window.FooterNavigation).TransformBounds(new Rect(window.OpenMainButton.RenderSize));
                Check(Math.Abs(left.Left) < 0.5 && Math.Abs(right.Right - window.FooterNavigation.ActualWidth) < 0.5 && left.Right < right.Left);
                foreach (var icon in HomeShellInspection.Descendants(window.FooterNavigation).OfType<ContextIcon>())
                    Check(!icon.Focusable && !icon.IsHitTestVisible && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(icon) is null);
            }
            Check(System.Windows.Automation.AutomationProperties.GetName(window.HelpButton) == "How this works" &&
                System.Windows.Automation.AutomationProperties.GetName(window.OpenMainButton) == "Open Offramp Shift");
            window.CloseAutomatically();
        });
        test("help and Back preserve geometry and commands and restore logical focus", () =>
        {
            var calls = 0; var switches = 0; var opens = 0;
            var window = new NudgeWindow(_ => { calls++; return Task.FromResult(HostCommandResult.Success()); },
                () => { switches++; return Task.FromResult(DesktopFeasibility.SwitchCompletion.Reject("Unavailable")); }, () => opens++);
            window.SetSwitchAvailability("");
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            Click(window.HelpButton);
            Check(window.ActionScroll.Visibility == Visibility.Collapsed && window.HelpView.Visibility == Visibility.Visible && window.HelpButton.Visibility == Visibility.Collapsed);
            Check(FocusManager.GetFocusedElement(window) == window.BackButton && window.Height == 300 && window.Width == 400);
            var help = (FrameworkElement)window.Content;
            help.Measure(new(384, 264)); help.Arrange(new(0, 0, 384, 264)); help.UpdateLayout();
            var copy = string.Join(" ", HomeShellInspection.Descendants(window.HelpView).OfType<TextBlock>().Select(x => x.Text));
            Check(copy.Contains("experimental") && copy.Contains("Win+Tab") && copy.Contains("not typing") && copy.Contains("while locked") && copy.Contains("1 minute") && copy.Contains("leaves this reminder open"));
            Click(window.OpenMainButton); Click(window.BackButton);
            Check(FocusManager.GetFocusedElement(window) == window.HelpButton && window.HelpView.Visibility == Visibility.Collapsed);
            Check(calls == 0 && switches == 0 && opens == 1 && window.CanReportFailure);
            window.CloseAutomatically();
        });
        foreach (var help in new[] { false, true })
            foreach (var action in new[] { "one", "five", "close", "escape" })
                test($"{action} from help={help} preserves exact durable duration", () =>
                {
                    var previous = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                    try
                    {
                        var durations = new List<long>();
                        var window = new NudgeWindow(duration => { durations.Add(duration); return Task.FromResult(HostCommandResult.Success()); });
                        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                        if (help) Click(window.HelpButton);
                        if (action == "one") { if (help) Click(window.BackButton); Click(window.OneMinute); }
                        else if (action == "five") { if (help) Click(window.BackButton); Click(window.FiveMinutes); }
                        else if (action == "close") window.Close();
                        else window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.Interop.HwndSource.FromHwnd(hwnd), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                        Drain();
                        Check(durations.SequenceEqual(new[] { action == "five" ? 300_000L : 60_000L }) && !window.CanReportFailure);
                    }
                    finally { SynchronizationContext.SetSynchronizationContext(previous); }
                });
        test("failure while help is open returns accessible operational feedback without resubmitting", () =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try
            {
                var pending = new TaskCompletionSource<HostCommandResult>(); var calls = 0;
                var window = new NudgeWindow(_ => { calls++; return pending.Task; });
                var operation = window.Submit(300_000);
                Click(window.HelpButton);
                Check(!window.OneMinute.IsEnabled && !window.FiveMinutes.IsEnabled && window.HelpView.Visibility == Visibility.Visible);
                pending.SetResult(HostCommandResult.Failure("Local storage is unavailable. Try again.")); Drain();
                Check(operation.IsCompleted && calls == 1 && window.HelpView.Visibility == Visibility.Collapsed && window.ActionScroll.Visibility == Visibility.Visible);
                Check(window.Result.Text.Contains("not saved") && window.Result.Text.Contains("Local storage") && window.OneMinute.IsEnabled);
                window.CloseAutomatically();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        });
        test("new availability problem exits help and retains consent reason", () =>
        {
            var window = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()));
            window.SetSwitchAvailability(""); Click(window.HelpButton);
            window.SetSwitchAvailability("Switching consent is required in Settings.");
            Check(window.HelpView.Visibility == Visibility.Collapsed && window.SwitchWarning.Text.Contains("consent") && window.SwitchWork.Visibility == Visibility.Collapsed);
            window.CloseAutomatically();
        });
        test("system contrast fallback retains actions and help without decorative artwork", () =>
        {
            var window = new NudgeWindow(_ => Task.FromResult(HostCommandResult.Success()));
            window.ApplyBrand(false, true); window.SetSwitchAvailability(""); Click(window.HelpButton);
            Check(window.FamilySymbol.Visibility == Visibility.Collapsed && ReferenceEquals(window.Resources["ShiftCanvas"], SystemColors.WindowBrush));
            Check(ReferenceEquals(window.SwitchWork.Background, SystemColors.WindowTextBrush) && ReferenceEquals(window.SwitchWork.Foreground, SystemColors.WindowBrush));
            Click(window.BackButton); Check(window.OneMinute.Focusable && window.HelpButton.Focusable);
            window.CloseAutomatically();
        });
    }
}
