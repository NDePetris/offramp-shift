using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OfframpShift.App;

internal sealed class NudgePlatform : INudgePlatform
{
    public nint ForegroundWindow => GetForegroundWindow();
    public string PlacementStatus { get; private set; } = "NotAttempted";
    internal bool DpiChangedDuringPlacement { get; private set; }
    internal static bool OnForegroundMonitor(nint hwnd, nint foreground) =>
        foreground != 0 && MonitorFromWindow(foreground, 0) is var monitor && monitor != 0 && MonitorFromWindow(hwnd, 0) == monitor;
    public bool Place(NudgeWindow window, nint foreground)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        PlacementStatus = "PreventImplicitActivation";
        // WPF's PerMonitorV2 DPI handler nests a SetWindowPos without NOACTIVATE.
        // Temporarily prohibit implicit activation on this HWND while positioning;
        // restore the exact style before Show/strict authorization/explicit focus.
        var placed = WithoutImplicitActivation(() => GetWindowLong(hwnd, -20),
            value => SetWindowLong(hwnd, -20, value), () => PlaceWithoutActivation(window, foreground), out var restored);
        if (!restored) PlacementStatus = "RestoreActivationStyle";
        return placed;
    }
    internal static bool WithoutImplicitActivation(Func<int> readStyle, Action<int> writeStyle,
        Func<bool> place, out bool restored)
    {
        var style = readStyle();
        var guarded = style | 0x08000000; // documented WS_EX_NOACTIVATE
        restored = false;
        var placed = false;
        try
        {
            writeStyle(guarded);
            if (readStyle() == guarded) placed = place();
        }
        finally
        {
            writeStyle(style);
            restored = readStyle() == style;
        }
        return placed && restored;
    }
    private bool PlaceWithoutActivation(NudgeWindow window, nint foreground)
    {
        PlacementStatus = "InitialForegroundValidation";
        if (foreground == 0 || GetForegroundWindow() != foreground) return false;
        PlacementStatus = "MonitorWorkArea";
        var monitor = MonitorFromWindow(foreground, 0);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return false;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        PlacementStatus = "InitialNonactivatingPosition";
        var previousDpi = GetDpiForWindow(hwnd);
        // Seed the HWND on the target monitor without activation, allowing WPF's
        // existing PerMonitorV2 handling to establish that monitor's own DPI.
        if (!SetWindowPos(hwnd, 0, info.Work.Left, info.Work.Top, 0, 0, 0x0015)) return false;
        PlacementStatus = "Dpi";
        var dpi = GetDpiForWindow(hwnd);
        DpiChangedDuringPlacement = dpi != previousDpi;
        if (dpi == 0) return false;
        var scale = dpi / 96d;
        PlacementStatus = "WorkAreaMinimumWidth";
        if (info.Work.Right - info.Work.Left < window.MinWidth * scale) return false;
        window.Width = Math.Min(400, (info.Work.Right - info.Work.Left) / scale);
        window.MaxHeight = (info.Work.Bottom - info.Work.Top) / scale;
        PlacementStatus = "NativeFrame";
        var frame = new Rectangle();
        if (!AdjustWindowRectExForDpi(ref frame, (uint)GetWindowLong(hwnd, -16), false, (uint)GetWindowLong(hwnd, -20), dpi)) return false;
        var frameWidth = frame.Right - frame.Left;
        var frameHeight = frame.Bottom - frame.Top;
        var clientWidth = window.Width - frameWidth / scale;
        var clientHeight = window.MaxHeight - frameHeight / scale;
        PlacementStatus = "ContentMeasurement";
        var contentHeight = MeasureContentHeight(window, clientWidth, clientHeight);
        if (contentHeight <= 0 || !double.IsFinite(contentHeight)) return false;
        var width = (int)Math.Ceiling(window.Width * scale);
        var height = (int)Math.Ceiling(contentHeight * scale) + frameHeight;
        PlacementStatus = "Centering";
        if (!TryCenter(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, width, height, out var area)) return false;
        PlacementStatus = "FinalForegroundValidation";
        if (GetForegroundWindow() != foreground) return false;
        PlacementStatus = "FinalNonactivatingPosition";
        if (!SetWindowPos(hwnd, 0, area.X, area.Y, area.Width, area.Height, 0x0014)) return false;
        PlacementStatus = "Placed";
        return true;
    }
    internal static double MeasureContentHeight(NudgeWindow window, double width, double height)
    {
        if (width <= 0 || height <= 0 || window.Content is not FrameworkElement content) return 0;
        // A never-shown Window is collapsed: Window.Measure returns DesiredSize=0.
        // Its visible content can be measured without Show or activation.
        content.Measure(new(width, height));
        return content.DesiredSize.Height;
    }
    internal static bool TryCenter(int left, int top, int right, int bottom, int width, int height,
        out (int X, int Y, int Width, int Height) area)
    {
        area = default;
        var availableWidth = (long)right - left; var availableHeight = (long)bottom - top;
        if (availableWidth <= 0 || availableHeight <= 0 || width <= 0 || height <= 0 ||
            availableWidth > int.MaxValue || availableHeight > int.MaxValue) return false;
        width = Math.Min(width, (int)availableWidth); height = Math.Min(height, (int)availableHeight);
        area = (left + ((int)availableWidth - width) / 2, top + ((int)availableHeight - height) / 2, width, height);
        return true;
    }
    public void Show(NudgeWindow window) => window.Show();
    public bool Activate(NudgeWindow window) => SetForegroundWindow(new WindowInteropHelper(window).Handle);
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rectangle Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustWindowRectExForDpi(ref Rectangle rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint extendedStyle, uint dpi);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
}
