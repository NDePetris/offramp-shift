using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OfframpShift.Core;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal static class Native
{
    internal delegate void WinEvent(nint hook, uint evt, nint window, int obj, int child, uint thread, uint time);
    internal delegate nint WindowProcedure(nint window, uint message, nuint w, nint l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size, Style;
        public WindowProcedure Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? Menu;
        public string Name;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message { public nint Window; public uint Id; public nuint W; public nint L; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInput { public uint Size, Time; }

    internal static (nint Window, uint Process, ApplicationIdentity? Application) Foreground()
    {
        var window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out var process) == 0 || process == 0) return (0, 0, null);
        using var handle = OpenProcess(0x1000, false, process); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle.IsInvalid) return (window, process, null);
        var image = new StringBuilder(32_768);
        var size = (uint)image.Capacity;
        if (!QueryFullProcessImageName(handle, 0, image, ref size) || size == 0) return (window, process, null);
        uint length = 0;
        var code = GetApplicationUserModelId(handle, ref length, null);
        string key;
        if (code == 122 && length is > 1 and <= 4096) // ERROR_INSUFFICIENT_BUFFER
        {
            var aumid = new StringBuilder((int)length);
            if (GetApplicationUserModelId(handle, ref length, aumid) != 0) return (window, process, null);
            key = "aumid:v1:" + Hash(aumid.ToString());
        }
        else if (code == 15703) // APPMODEL_ERROR_NO_APPLICATION
            key = "exe:v1:" + Hash(image.ToString().ToUpperInvariant());
        else return (window, process, null);
        if (!GetExitCodeProcess(handle, out var exit) || exit != 259 ||
            GetForegroundWindow() != window || GetWindowThreadProcessId(window, out var after) == 0 || after != process)
            return (window, process, null);
        // Identifies the process, not a hosted app behind a broker. No Core classification is asserted.
        return (window, process, new(key, true));
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static SessionState Session()
    {
        var expected = (uint)System.Diagnostics.Process.GetCurrentProcess().SessionId;
        if (!WTSQuerySessionInformation(0, expected, 25, out var buffer, out var bytes)) return SessionState.Unknown;
        try
        {
            // WTSINFOEX level 1, x64 SDK layout: union aligned at 8; ID/state/flags at 8/12/16.
            // Read only these scalars. Do not marshal user/domain names or wall-clock activity fields.
            if (bytes < 20 || Marshal.ReadInt32(buffer) != 1 || (uint)Marshal.ReadInt32(buffer, 8) != expected ||
                Marshal.ReadInt32(buffer, 12) != 0) return SessionState.Unknown; // WTSActive
            return Marshal.ReadInt32(buffer, 16) switch { 0 => SessionState.Locked, 1 => SessionState.Active, _ => SessionState.Unknown };
        }
        finally { WTSFreeMemory(buffer); }
    }

    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern ushort RegisterClassEx(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint CreateWindowEx(uint extended, string name, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint DefWindowProc(nint window, uint message, nuint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nuint w, nint l);
    [DllImport("user32.dll")] internal static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LastInput value);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("kernel32.dll")] internal static extern bool QueryUnbiasedInterruptTime(out ulong time);
    [DllImport("kernel32.dll")] internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint process);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? id);
    [DllImport("kernel32.dll")] internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exit);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("wtsapi32.dll")] internal static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")] internal static extern bool WTSUnRegisterSessionNotification(nint window);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")] internal static extern bool WTSQuerySessionInformation(nint server, uint session, uint kind, out nint buffer, out uint bytes);
    [DllImport("wtsapi32.dll")] internal static extern void WTSFreeMemory(nint buffer);
    [DllImport("user32.dll")] internal static extern nint RegisterSuspendResumeNotification(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnregisterSuspendResumeNotification(nint registration);
}
