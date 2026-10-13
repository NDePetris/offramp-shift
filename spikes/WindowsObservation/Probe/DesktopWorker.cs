using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal sealed class DesktopWorker : IDisposable
{
    private Process? process;
    private bool failed;
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
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
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

    // Private stdin protocol has one read command; no binding or mutation operation.
    public static int Run()
    {
        if (!IsSupported(Build, RuntimeInformation.ProcessArchitecture)) return 2;
        try
        {
            using var reader = new DesktopComReader();
            while (Console.ReadLine() is { } request)
            {
                if (request != "read") return 2;
                Console.WriteLine(JsonSerializer.Serialize(reader.Read()));
            }
            return 0;
        }
        catch { Console.WriteLine(JsonSerializer.Serialize(new DesktopReading(Capability.Unavailable, null, [], 0))); return 1; }
    }
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
