using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BrowserAttribution;

internal sealed class WindowsObserver
{
    private readonly ConcurrentDictionary<long, long> generations = new();
    private long epoch;
    private readonly HookCallback callback;
    public long Epoch => Interlocked.Read(ref epoch);
    public WindowsObserver()
    {
        callback = (_, evt, hwnd, obj, child, _, _) =>
        {
            if (evt == 3) Interlocked.Increment(ref epoch);
            if (evt == 0x8001 && obj == 0 && child == 0 && hwnd != 0)
                generations.AddOrUpdate(hwnd.ToInt64(), 1, (_, n) => n + 1);
        };
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            // OUTOFCONTEXT hooks need a message loop; no UI or title access.
            nint focus = SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
            nint destroy = SetWinEventHook(0x8001, 0x8001, 0, callback, 0, 0, 0);
            if (focus == 0 || destroy == 0) failure = new InvalidOperationException("Window hooks unavailable");
            ready.Set();
            if (failure != null) return;
            while (GetMessage(out _, 0, 0, 0) > 0) { }
        }) { IsBackground = true, Name = "foreground-hooks" };
        thread.Start(); ready.Wait();
        if (failure != null) throw failure;
    }
    public Window Foreground() => Inspect(GetForegroundWindow().ToInt64());
    public (Window Window, long Epoch) Observation()
    {
        long before = Epoch;
        var w = Foreground(); long after = Epoch;
        if (before != after || GetForegroundWindow().ToInt64() != w.Hwnd) w = w with { Eligible = false };
        return (w, after);
    }
    public Window Inspect(long hwnd)
    {
        nint handle = (nint)hwnd;
        GetWindowThreadProcessId(handle, out uint pid);
        string app = "unknown"; long birth = 0;
        try { using var p = Process.GetProcessById((int)pid); app = p.ProcessName.ToLowerInvariant(); birth = p.StartTime.ToUniversalTime().Ticks; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        var cls = new StringBuilder(128);
        GetClassName(handle, cls, cls.Capacity);
        int result = DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int));
        bool eligible = hwnd != 0 && IsWindow(handle) && IsWindowVisible(handle) && !IsIconic(handle) &&
            result == 0 && cloaked == 0 && birth > 0 && app is "chrome" or "msedge" && cls.ToString() == "Chrome_WidgetWin_1";
        return new(hwnd, (int)pid, birth, generations.GetValueOrDefault(hwnd), app, eligible);
    }
    public static bool BrowserAncestor(int childPid, int browserPid)
    {
        nint snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == -1) return false;
        try
        {
            var parents = new Dictionary<int, int>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry)) do { parents[(int)entry.Pid] = (int)entry.Parent; } while (Process32Next(snapshot, ref entry));
            for (int i = 0; i < 8 && parents.TryGetValue(childPid, out int parent); i++)
            { if (parent == browserPid) return true; childPid = parent; }
            return false;
        }
        finally { CloseHandle(snapshot); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Pid; public nuint Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public nint Hwnd; public uint Message; public nuint W; public nint L; public uint Time; public int X, Y; public uint Private; }
    private delegate void HookCallback(nint hook, uint evt, nint hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, HookCallback callback, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder value, int count);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
