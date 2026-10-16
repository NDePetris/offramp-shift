using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Globalization;
using Microsoft.Win32;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal sealed class DesktopWorker : IDisposable
{
    private Process? process;
    private bool failed;
    public Process? OwnedProcess => process;
    public static bool IsSupported(string build, Architecture architecture) => DesktopObservationPolicy.IsSupported(build, architecture.ToString());
    public static string Build
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return $"{key?.GetValue("CurrentBuild")}.{key?.GetValue("UBR")}";
        }
    }

    public DesktopWorker(string mode = "--desktop-worker")
    {
        if (!IsSupported(Build, RuntimeInformation.ProcessArchitecture)) return;
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("ExecutableUnavailable");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        start.ArgumentList.Add(mode);
        try { process = Process.Start(start) ?? throw new InvalidOperationException(); }
        catch { failed = true; }
    }

    public DesktopReading Read(int deadlineMs = 2_000)
    {
        if (failed) return new(Capability.Unavailable, null, [], 0);
        if (process is null) return new(Capability.Unsupported, null, [], 0);
        try
        {
            if (process.HasExited) throw new InvalidOperationException();
            process.StandardInput.WriteLine("read");
            process.StandardInput.Flush();
            var line = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(deadlineMs)).GetAwaiter().GetResult();
            if (line is null || line.Length > 4_096) throw new InvalidOperationException();
            var result = JsonSerializer.Deserialize<DesktopReading>(line) ?? throw new InvalidOperationException();
            if (result.Health != Capability.Available || result.Current is not { } current || current == Guid.Empty ||
                result.Desktops is null || result.Desktops.Length is < 1 or > 64 || result.Desktops.Any(id => id == Guid.Empty) ||
                result.Desktops.Distinct().Count() != result.Desktops.Length || !result.Desktops.Contains(current) || result.ShellProcess == 0)
                throw new InvalidOperationException();
            return result;
        }
        catch { failed = true; Dispose(); return new(Capability.Unavailable, null, [], 0); }
    }

    public void Dispose()
    {
        var owned = process;
        if (owned is null) return;
        try
        {
            WorkerShutdown.Stop(() => owned.HasExited, owned.Kill, () => owned.WaitForExit(1_000));
        }
        finally { owned.Dispose(); process = null; failed = true; }
    }

    public bool CheckNudge(nint window, nint foreground, Guid personal)
    {
        if (failed || process is null || window == 0 || foreground == 0 || personal == Guid.Empty) return false;
        try
        {
            process.StandardInput.WriteLine(FormattableString.Invariant($"nudge {window.ToInt64()} {foreground.ToInt64()} {personal:D}"));
            process.StandardInput.Flush();
            var line = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(2_000)).GetAwaiter().GetResult();
            if (line is not ("true" or "false")) throw new InvalidOperationException();
            return line == "true";
        }
        catch { failed = true; Dispose(); return false; }
    }

    // Private stdin protocol only reads observation/window membership; no mutation operation.
    public static int Run()
    {
        if (!IsSupported(Build, RuntimeInformation.ProcessArchitecture)) return 2;
        try
        {
            using var reader = new DesktopComReader();
            while (Console.ReadLine() is { } request)
            {
                if (request == "read") Console.WriteLine(JsonSerializer.Serialize(reader.Read()));
                else if (request.Length <= 160 && request.Split(' ') is ["nudge", var window, var foreground, var desktop] &&
                    long.TryParse(window, NumberStyles.None, CultureInfo.InvariantCulture, out var hwnd) && hwnd > 0 &&
                    long.TryParse(foreground, NumberStyles.None, CultureInfo.InvariantCulture, out var active) && active > 0 &&
                    Guid.TryParseExact(desktop, "D", out var personal) && personal != Guid.Empty)
                    Console.WriteLine(WindowDesktopMembership.Check(reader, (nint)hwnd, (nint)active, personal) ? "true" : "false");
                else return 2;
            }
            return 0;
        }
        catch { Console.WriteLine(JsonSerializer.Serialize(new DesktopReading(Capability.Unavailable, null, [], 0))); return 1; }
    }
}

// Documented public interface, deliberately ending before MoveWindowToDesktop.
// Shell calls remain in the existing build-gated, deadline-isolated child process.
[SupportedOSPlatform("windows")]
internal static class WindowDesktopMembership
{
    internal static bool Check(DesktopComReader reader, nint window, nint foreground, Guid personal)
    {
        IWindowDesktopMembership? membership = null;
        try
        {
            if (Native.GetForegroundWindow() != foreground || Native.Session() != OfframpShift.Core.SessionState.Active) return false;
            var before = reader.Read();
            if (before.Current != personal) return false;
            membership = (IWindowDesktopMembership)Activator.CreateInstance(Type.GetTypeFromCLSID(new("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true)!)!;
            Marshal.ThrowExceptionForHR(membership.IsWindowOnCurrentVirtualDesktop(window, out var onCurrent));
            Marshal.ThrowExceptionForHR(membership.GetWindowDesktopId(window, out var windowDesktop));
            Marshal.ThrowExceptionForHR(membership.GetWindowDesktopId(foreground, out var foregroundDesktop));
            var after = reader.Read();
            return onCurrent && windowDesktop == personal && foregroundDesktop == personal && after.Current == personal &&
                before.ShellProcess == after.ShellProcess && Native.GetForegroundWindow() == foreground &&
                Native.Session() == OfframpShift.Core.SessionState.Active;
        }
        catch { return false; }
        finally { if (membership is not null) Marshal.ReleaseComObject(membership); }
    }
}

[ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowDesktopMembership
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool onCurrent);
    [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
}

// ABI facts follow VirtualDesktopAccessor ceb648849dcc5760a7f4ed223308ffe3de0b51c6.
// Unlike the switching spike, the manager declaration ENDS before any switch slot.
[SupportedOSPlatform("windows")]
internal sealed class DesktopComReader : IDisposable
{
    private IShellServices? shell;
    private IReadOnlyManager? manager;
    private readonly uint shellGeneration;
    public DesktopComReader()
    {
        try
        {
            Native.GetWindowThreadProcessId(Native.GetShellWindow(), out shellGeneration);
            if (shellGeneration == 0) throw new InvalidOperationException();
            shell = (IShellServices)Activator.CreateInstance(Type.GetTypeFromCLSID(new("C2F03A33-21F5-47FA-B4BB-156362A2F239"), true)!)!;
            var service = new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
            var iid = typeof(IReadOnlyManager).GUID;
            Marshal.ThrowExceptionForHR(shell.QueryService(ref service, ref iid, out var pointer));
            try { manager = (IReadOnlyManager)Marshal.GetObjectForIUnknown(pointer); }
            finally { Marshal.Release(pointer); }
        }
        catch { Dispose(); throw; }
    }
    public DesktopReading Read()
    {
        Native.GetWindowThreadProcessId(Native.GetShellWindow(), out var shellBefore);
        if (shellBefore == 0 || shellBefore != shellGeneration) throw new InvalidOperationException();
        var before = Current();
        Marshal.ThrowExceptionForHR(manager!.GetDesktops(out var array));
        var ids = new List<Guid>();
        try
        {
            Marshal.ThrowExceptionForHR(array.GetCount(out var count));
            if (count is < 1 or > 64) throw new InvalidOperationException();
            var iid = typeof(IReadOnlyDesktop).GUID;
            for (uint i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(array.GetAt(i, ref iid, out var desktop));
                try { Marshal.ThrowExceptionForHR(desktop.GetId(out var id)); ids.Add(id); }
                finally { Marshal.ReleaseComObject(desktop); }
            }
        }
        finally { Marshal.ReleaseComObject(array); }
        var after = Current();
        Native.GetWindowThreadProcessId(Native.GetShellWindow(), out var shellAfter);
        if (before != after || shellBefore != shellAfter) throw new InvalidOperationException();
        return new(Capability.Available, before, ids.ToArray(), shellAfter);
    }
    private Guid Current()
    {
        Marshal.ThrowExceptionForHR(manager!.GetCurrentDesktop(out var desktop));
        try { Marshal.ThrowExceptionForHR(desktop.GetId(out var id)); return id; }
        finally { Marshal.ReleaseComObject(desktop); }
    }
    public void Dispose()
    {
        if (manager is not null) { Marshal.ReleaseComObject(manager); manager = null; }
        if (shell is not null) { Marshal.ReleaseComObject(shell); shell = null; }
    }
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellServices { [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out nint value); }
[ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IReadOnlyManager
{
    [PreserveSig] int GetCount(out uint count);
    void UnusedSlot1();
    void UnusedSlot2();
    [PreserveSig] int GetCurrentDesktop(out IReadOnlyDesktop desktop);
    [PreserveSig] int GetDesktops(out IReadOnlyArray array);
}
[ComImport, Guid("3F07F4BE-B107-441A-AF0F-39D82529072C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IReadOnlyDesktop { void UnusedSlot0(); [PreserveSig] int GetId(out Guid id); }
[ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IReadOnlyArray
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, ref Guid iid, out IReadOnlyDesktop desktop);
}
