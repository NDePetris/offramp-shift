using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using DesktopFeasibility;

namespace DesktopProbe;

// All undocumented COM calls live in the disposable worker process.
// ABI reference: Ciantic/VirtualDesktopAccessor @ ceb648849dcc5760a7f4ed223308ffe3de0b51c6.
[SupportedOSPlatform("windows")]
internal sealed class NativeSession : IDesktopSession, IDisposable
{
    private readonly Configuration configuration;
    private readonly IServiceProvider shell;
    private readonly IManager manager;
    private readonly IPublicManager publicManager;
    private IVirtualDesktop? resolved;

    public NativeSession()
    {
        configuration = ReadConfiguration();
        if (!Capabilities.MayProbe(configuration)) throw new InvalidOperationException("Undocumented ABI disabled for this exact Windows build/architecture.");
        shell = (IServiceProvider)Activator.CreateInstance(Type.GetTypeFromCLSID(new("C2F03A33-21F5-47FA-B4BB-156362A2F239"), true)!)!;
        var service = new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
        var iid = typeof(IManager).GUID;
        Check(shell.QueryService(ref service, ref iid, out var pointer));
        try { manager = (IManager)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
        publicManager = (IPublicManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true)!)!;
    }

    public static Configuration ReadConfiguration()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = key?.GetValue("CurrentBuild")?.ToString() ?? "unknown";
        var revision = key?.GetValue("UBR")?.ToString() ?? "unknown";
        return new(build + "." + revision, RuntimeInformation.ProcessArchitecture.ToString(),
            Process.GetCurrentProcess().SessionId, GetSystemMetrics(80));
    }

    public Snapshot Observe()
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(GetShellWindow(), out var shellPid);
        Check(manager.GetCurrentDesktop(out var current));
        Guid currentId;
        try { Check(current.GetId(out currentId)); }
        finally { Release(current); }
        Check(manager.GetDesktops(out var array));
        var desktops = new List<Desktop>();
        try
        {
            Check(array.GetCount(out var count));
            if (count is < 1 or > 64) throw new InvalidOperationException("Invalid desktop count.");
            var iid = typeof(IVirtualDesktop).GUID;
            for (uint i = 0; i < count; i++)
            {
                Check(array.GetAt(i, ref iid, out var desktop));
                try
                {
                    Check(desktop.GetId(out var id));
                    Check(desktop.GetName(out var text));
                    string name;
                    try
                    {
                        var buffer = WindowsGetStringRawBuffer(text, out var length);
                        if (length > 4096) throw new InvalidOperationException("Oversized desktop name.");
                        name = buffer == 0 ? "" : Marshal.PtrToStringUni(buffer, (int)length) ?? "";
                    }
                    finally { WindowsDeleteString(text); }
                    desktops.Add(new(id, name));
                }
                finally { Release(desktop); }
            }
        }
        finally { Release(array); }
        // Corroboration only: foreground window membership is not the current identity.
        Guid? foregroundId = null;
        bool? onCurrent = null;
        if (hwnd != 0)
        {
            if (publicManager.GetWindowDesktopId(hwnd, out var id) >= 0 && id != Guid.Empty) foregroundId = id;
            if (publicManager.IsWindowOnCurrentVirtualDesktop(hwnd, out var value) >= 0) onCurrent = value;
        }
        // Detect a transition during enumeration; never publish a mixed snapshot as valid.
        Check(manager.GetCurrentDesktop(out current));
        try { Check(current.GetId(out var after)); if (after != currentId) throw new InvalidOperationException("Desktop changed during observation."); }
        finally { Release(current); }
        var snapshot = new Snapshot(ReadConfiguration(), Capabilities.Provider, DateTimeOffset.UtcNow, Environment.TickCount64,
            currentId, desktops.ToArray(), hwnd.ToInt64(), checked((int)shellPid), IsInteractive(), foregroundId, onCurrent);
        Safety.ValidateSnapshot(snapshot);
        return snapshot;
    }

    public Guid ResolveIdentity(Guid target)
    {
        if (resolved != null) Release(resolved);
        resolved = null;
        Check(manager.FindDesktop(ref target, out var desktop));
        resolved = desktop;
        Check(desktop.GetId(out var id));
        return id;
    }

    public void SwitchByIdentity(Guid target)
    {
        if (resolved == null) throw new InvalidOperationException("Destination has not been resolved.");
        Check(resolved.GetId(out var id));
        if (id != target) throw new InvalidOperationException("Destination identity changed.");
        // Deliberately NOT SwitchDesktopAndMoveForegroundView; never switch by ordinal.
        Check(manager.SwitchDesktop(resolved));
    }

    private bool IsInteractive()
    {
        if (!WTSQuerySessionInformation(0, configuration.SessionId, 8, out var buffer, out var bytes)) return false;
        try { if (bytes < 4 || Marshal.ReadInt32(buffer) != 0) return false; }
        finally { WTSFreeMemory(buffer); }
        var input = OpenInputDesktop(0, false, 1);
        if (input == 0) return false;
        try
        {
            var name = new char[256];
            return GetUserObjectInformation(input, 2, name, name.Length * 2, out _)
                && new string(name).TrimEnd('\0').Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(input); }
    }

    public void Dispose()
    {
        if (resolved != null) Release(resolved);
        Release(publicManager);
        Release(manager);
        Release(shell);
    }
    private static void Release(object value) { if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUserObjectInformation(nint handle, int index, [Out] char[] data, int size, out int needed);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSQuerySessionInformation(nint server, int session, int info, out nint buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint buffer);
    [DllImport("combase.dll")] private static extern nint WindowsGetStringRawBuffer(nint text, out uint length);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint text);
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out nint value);
}

// Unused slots exist solely to preserve the pinned ABI's vtable order.
[ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IManager
{
    [PreserveSig] int GetCount(out uint count);
    void UnusedMoveView();
    void UnusedCanMoveView();
    [PreserveSig] int GetCurrentDesktop(out IVirtualDesktop desktop);
    [PreserveSig] int GetDesktops(out IObjectArray array);
    void UnusedAdjacentDesktop();
    [PreserveSig] int SwitchDesktop(IVirtualDesktop desktop);
    void UnusedSwitchAndMoveForeground();
    void UnusedCreateDesktop();
    void UnusedMoveDesktop();
    void UnusedRemoveDesktop();
    [PreserveSig] int FindDesktop(ref Guid id, out IVirtualDesktop desktop);
}

[ComImport, Guid("3F07F4BE-B107-441A-AF0F-39D82529072C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktop
{
    void UnusedIsViewVisible();
    [PreserveSig] int GetId(out Guid id);
    [PreserveSig] int GetName(out nint name);
}

[ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, ref Guid iid, out IVirtualDesktop desktop);
}

[ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPublicManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
    [PreserveSig] int GetWindowDesktopId(nint window, out Guid id);
    // No window-movement method is exposed by the probe.
}
