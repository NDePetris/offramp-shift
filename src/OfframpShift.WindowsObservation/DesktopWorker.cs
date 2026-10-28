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
    public static bool IsSupported(string build, Architecture architecture) => RuntimeInformation.OSArchitecture == Architecture.X64 &&
        DesktopObservationPolicy.IsSupported(build, architecture.ToString());
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

    public bool CheckNudge(nint window, nint foreground, Guid personal) => CheckNudgeStatus(window, foreground, personal) == NudgeDesktopCheck.Allowed;

    public NudgeDesktopCheck CheckNudgeStatus(nint window, nint foreground, Guid personal, bool prepareOnly = false)
        => RequestNudge(window, foreground, personal, prepareOnly, null).Result;

    public NudgeCheckResponse CaptureNudgeStatus(nint window, nint foreground, Guid personal, Guid work)
        => RequestNudge(window, foreground, personal, false, work);

    private NudgeCheckResponse RequestNudge(nint window, nint foreground, Guid personal, bool prepareOnly, Guid? captureWork)
    {
        NudgeCheckResponse Unavailable() => new(NudgeDesktopCheck.WorkerUnavailable, captureWork is null ? null : new()
        {
            Stage = !IsSupported(Build, RuntimeInformation.ProcessArchitecture) ? NudgeCheckStage.Unsupported : NudgeCheckStage.WorkerUnavailable
        });
        if (failed || process is null || window == 0 || foreground == 0 || personal == Guid.Empty) return Unavailable();
        try
        {
            var operation = captureWork is not null ? "capture-nudge" : prepareOnly ? "prepare-nudge" : "nudge";
            var request = FormattableString.Invariant($"{operation} {window.ToInt64()} {foreground.ToInt64()} {personal:D}");
            if (captureWork is { } work) request += " " + work.ToString("D");
            process.StandardInput.WriteLine(request);
            process.StandardInput.Flush();
            var line = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(2_000)).GetAwaiter().GetResult();
            if (line is null || line.Length > 4_096) throw new InvalidOperationException();
            if (captureWork is not null)
            {
                var response = JsonSerializer.Deserialize<NudgeCheckResponse>(line) ?? throw new InvalidOperationException();
                if (!Enum.IsDefined(response.Result) || response.Diagnostic is null || response.Diagnostic.Result != response.Result)
                    throw new InvalidOperationException();
                return response;
            }
            if (!Enum.TryParse<NudgeDesktopCheck>(line, out var result) || !Enum.IsDefined(result) || line != result.ToString()) throw new InvalidOperationException();
            return new(result, null);
        }
        catch { failed = true; Dispose(); return Unavailable(); }
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
                else if (request.Length <= 160 && request.Split(' ') is ["capture-nudge", var capturedWindow, var capturedForeground, var p, var w] &&
                    long.TryParse(capturedWindow, NumberStyles.None, CultureInfo.InvariantCulture, out var cw) && cw > 0 &&
                    long.TryParse(capturedForeground, NumberStyles.None, CultureInfo.InvariantCulture, out var cf) && cf > 0 &&
                    Guid.TryParseExact(p, "D", out var savedPersonal) && savedPersonal != Guid.Empty &&
                    Guid.TryParseExact(w, "D", out var savedWork) && savedWork != Guid.Empty && savedWork != savedPersonal)
                {
                    var diagnostic = new NudgeDesktopDiagnostic { Role = NudgeWindowRole.OriginalForeground };
                    var result = WindowDesktopMembership.Check(reader, (nint)cw, (nint)cf, savedPersonal, false, diagnostic, savedWork);
                    Console.WriteLine(JsonSerializer.Serialize(new NudgeCheckResponse(result, diagnostic)));
                }
                else if (request.Length <= 160 && request.Split(' ') is [var operation, var window, var foreground, var desktop] &&
                    operation is "nudge" or "prepare-nudge" &&
                    long.TryParse(window, NumberStyles.None, CultureInfo.InvariantCulture, out var hwnd) && hwnd > 0 &&
                    long.TryParse(foreground, NumberStyles.None, CultureInfo.InvariantCulture, out var active) && active > 0 &&
                    Guid.TryParseExact(desktop, "D", out var personal) && personal != Guid.Empty)
                    Console.WriteLine(WindowDesktopMembership.Check(reader, (nint)hwnd, (nint)active, personal, operation == "prepare-nudge"));
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
    internal static NudgeDesktopCheck Check(DesktopComReader reader, nint window, nint foreground, Guid personal, bool prepareOnly,
        NudgeDesktopDiagnostic? diagnostic = null, Guid work = default)
    {
        using var queries = new MembershipQueries(reader);
        return Check(queries, window, foreground, personal, prepareOnly, diagnostic, work);
    }

    internal static NudgeDesktopCheck Check(IWindowMembershipQueries queries, nint window, nint foreground, Guid personal, bool prepareOnly,
        NudgeDesktopDiagnostic? diagnostic = null, Guid work = default)
    {
        void Stage(NudgeCheckStage stage) { if (diagnostic is not null) diagnostic.Stage = stage; }
        NudgeDesktopCheck Done(NudgeDesktopCheck result) { if (diagnostic is not null) diagnostic.Result = result; return result; }
        static DiagnosticCheck State(bool value) => value ? DiagnosticCheck.Passed : DiagnosticCheck.Failed;
        try
        {
            Stage(NudgeCheckStage.ForegroundBefore);
            var foregroundBefore = queries.Foreground == foreground;
            if (diagnostic is not null) diagnostic.ForegroundBefore = State(foregroundBefore);
            if (!foregroundBefore) return Done(NudgeDesktopCheck.ForegroundChanged);
            Stage(NudgeCheckStage.SessionBefore);
            var sessionBeforeState = queries.Session;
            var sessionBefore = sessionBeforeState == OfframpShift.Core.SessionState.Active;
            if (diagnostic is not null) diagnostic.SessionBefore = sessionBeforeState == OfframpShift.Core.SessionState.Unknown ? DiagnosticCheck.Unavailable : State(sessionBefore);
            if (!sessionBefore) return Done(NudgeDesktopCheck.SessionUnavailable);
            Stage(NudgeCheckStage.DesktopBefore);
            var before = queries.Read();
            if (diagnostic is not null)
            {
                diagnostic.CurrentBefore = NudgeDesktopDiagnostic.Categorize(0, before.Current, personal, work, before.Desktops);
                diagnostic.PersonalBefore = State(before.Current == personal);
            }
            if (before.Current != personal) return Done(NudgeDesktopCheck.PersonalDesktopChanged);
            Stage(NudgeCheckStage.CurrentMembership);
            var currentCode = queries.IsOnCurrent(window, out var onCurrent);
            if (diagnostic is not null) { diagnostic.CurrentMembershipCode = currentCode; diagnostic.OnCurrent = currentCode >= 0 ? onCurrent : null; }
            Marshal.ThrowExceptionForHR(currentCode);
            Stage(NudgeCheckStage.CandidateIdentity);
            var identityCode = queries.GetIdentity(window, out var windowDesktop);
            if (diagnostic is not null)
            {
                diagnostic.HostingIdentityCode = identityCode;
                diagnostic.HostingIdentity = NudgeDesktopDiagnostic.Categorize(identityCode, windowDesktop, personal, work, before.Desktops);
            }
            // Windows may not register a new hidden HWND until its first Show.
            // This exception authorizes only a nonactivating first Show, never focus.
            var visible = queries.Visible(window); // Existing query, same order and count.
            if (diagnostic is not null)
            {
                diagnostic.VisibleWindow = visible;
                // Extra read-only HWND metadata only when explicitly armed; never an authorization input.
                try { diagnostic.ValidWindow = queries.Valid(window); diagnostic.TopLevelWindow = queries.TopLevel(window); }
                catch { /* Diagnostic metadata failure cannot affect the original result. */ }
            }
            var candidate = CheckCandidate(onCurrent, identityCode, windowDesktop, personal, prepareOnly, visible);
            if (candidate != NudgeDesktopCheck.Allowed) return Done(candidate);
            Stage(NudgeCheckStage.ForegroundIdentity);
            var foregroundCode = queries.GetIdentity(foreground, out var foregroundDesktop);
            if (diagnostic is not null)
            {
                diagnostic.ForegroundIdentityCode = foregroundCode;
                diagnostic.ForegroundIdentity = NudgeDesktopDiagnostic.Categorize(foregroundCode, foregroundDesktop, personal, work, before.Desktops);
            }
            if (foregroundCode < 0) return Done(NudgeDesktopCheck.ForegroundIdentityUnavailable);
            if (foregroundDesktop != personal) return Done(NudgeDesktopCheck.ForegroundWrongDesktop);
            Stage(NudgeCheckStage.DesktopAfter);
            var after = queries.Read();
            if (diagnostic is not null)
            {
                diagnostic.CurrentAfter = NudgeDesktopDiagnostic.Categorize(0, after.Current, personal, work, after.Desktops);
                diagnostic.PersonalAfter = State(after.Current == personal);
                diagnostic.ShellStable = State(before.ShellProcess == after.ShellProcess);
                diagnostic.TopologyStable = State(before.Desktops.ToHashSet().SetEquals(after.Desktops));
            }
            if (after.Current != personal || before.ShellProcess != after.ShellProcess) return Done(NudgeDesktopCheck.PersonalDesktopChanged);
            Stage(NudgeCheckStage.ForegroundAfter);
            var foregroundAfter = queries.Foreground == foreground;
            if (diagnostic is not null) diagnostic.ForegroundAfter = State(foregroundAfter);
            if (!foregroundAfter) return Done(NudgeDesktopCheck.ForegroundChanged);
            Stage(NudgeCheckStage.SessionAfter);
            var sessionAfterState = queries.Session;
            var sessionAfter = sessionAfterState == OfframpShift.Core.SessionState.Active;
            if (diagnostic is not null) diagnostic.SessionAfter = sessionAfterState == OfframpShift.Core.SessionState.Unknown ? DiagnosticCheck.Unavailable : State(sessionAfter);
            if (!sessionAfter) return Done(NudgeDesktopCheck.SessionUnavailable);
            Stage(NudgeCheckStage.Allowed);
            return Done(NudgeDesktopCheck.Allowed);
        }
        catch { return Done(NudgeDesktopCheck.QueryUnavailable); }
    }
    internal static NudgeDesktopCheck CheckCandidate(bool onCurrent, int identityCode, Guid desktop, Guid personal, bool prepareOnly, bool visible)
    {
        if (!onCurrent) return NudgeDesktopCheck.CandidateNotCurrent;
        if (identityCode < 0)
            return prepareOnly && !visible && identityCode == unchecked((int)0x8002802B) ? NudgeDesktopCheck.Allowed : NudgeDesktopCheck.CandidateIdentityUnavailable;
        return desktop == personal && personal != Guid.Empty ? NudgeDesktopCheck.Allowed : NudgeDesktopCheck.CandidateWrongDesktop;
    }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);

    private sealed class MembershipQueries(DesktopComReader reader) : IWindowMembershipQueries, IDisposable
    {
        private IWindowDesktopMembership? membership;
        private IWindowDesktopMembership Membership => membership ??= (IWindowDesktopMembership)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true)!)!;
        public nint Foreground => Native.GetForegroundWindow();
        public OfframpShift.Core.SessionState Session => Native.Session();
        public DesktopReading Read() => reader.Read();
        public int IsOnCurrent(nint window, out bool current) => Membership.IsWindowOnCurrentVirtualDesktop(window, out current);
        public int GetIdentity(nint window, out Guid identity) => Membership.GetWindowDesktopId(window, out identity);
        public bool Visible(nint window) => IsWindowVisible(window);
        public bool Valid(nint window) => IsWindow(window);
        public bool TopLevel(nint window) => GetAncestor(window, 2) == window;
        public void Dispose() { if (membership is not null) Marshal.ReleaseComObject(membership); }
    }
}

internal interface IWindowMembershipQueries
{
    nint Foreground { get; }
    OfframpShift.Core.SessionState Session { get; }
    DesktopReading Read();
    int IsOnCurrent(nint window, out bool current);
    int GetIdentity(nint window, out Guid identity);
    bool Visible(nint window);
    bool Valid(nint window);
    bool TopLevel(nint window);
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
