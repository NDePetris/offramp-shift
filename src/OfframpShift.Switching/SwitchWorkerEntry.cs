using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DesktopProbe;

namespace DesktopFeasibility;

public static class SwitchWorkerEntry
{
    [SupportedOSPlatform("windows")]
    public static int Run(bool qualification = false)
    {
        // Owned-process watchdog also bounds a blocked STA if the parent dies.
        // Exit cannot undo a request already accepted by Explorer.
        using var lifetime = new Timer(_ => Environment.Exit(124), null, 3_000, Timeout.Infinite);
        return RunRequest(Console.In, Console.Out, qualification,
            Thread.CurrentThread.GetApartmentState() == ApartmentState.STA,
            NativeSession.ReadConfiguration, () => new NativeSession(qualification), PumpDelay);
    }

    // Internal seam exercises the production entry guards without shell activation.
    internal static int RunRequest(TextReader input, TextWriter output, bool qualification, bool sta,
        Func<Configuration> configuration, Func<IDesktopSession> openSession, Action<int> delay)
    {
        try
        {
            if (!sta)
                throw new InvalidOperationException("STA worker required.");
            var text = new char[SwitchWorkerProcess.MaximumCharacters + 1];
            var length = input.ReadBlock(text, 0, text.Length);
            if (length > SwitchWorkerProcess.MaximumCharacters) throw new InvalidDataException("Request oversized.");
            var request = SwitchProtocol.Decode<SwitchWorkerRequest>(new string(text, 0, length));
            if (request.Command != "switch" && !(qualification && request.Command == "capture"))
                throw new InvalidDataException("Unknown operation.");
            if (request.Command == "switch" && request.Authorization is not { Valid: true })
                throw new InvalidDataException("Missing authorization.");
            if (request.Command == "capture" && request.Authorization is not null)
                throw new InvalidDataException("Unexpected authorization.");
            if (request.Authorization is { } authorization)
            {
                // Validate the complete expected topology, protocol and lease
                // before activating any undocumented COM object.
                var rejection = Safety.Reject(authorization.Request, authorization.Request.Expected,
                    DateTimeOffset.UtcNow, Environment.TickCount64, qualification);
                if (rejection is null && configuration() != authorization.Request.Expected.Configuration)
                    rejection = "Configuration changed.";
                if (rejection is not null)
                {
                    output.WriteLine(Wire.Encode(new SwitchWorkerReply(new(authorization.Request.RequestId,
                        "Rejected: " + rejection, null, false, 0))));
                    return 0;
                }
            }
            // Gate before activation, independently of all parent/settings assertions.
            if (!SwitchCompatibility.Allowed(configuration(), qualification))
                throw new InvalidOperationException("Unsupported exact build/architecture.");
            var session = openSession();
            using var disposal = session as IDisposable;
            SwitchWorkerReply reply;
            if (request.Command == "capture") reply = new(Snapshot: session.Observe());
            else
            {
                var clock = Stopwatch.StartNew();
                reply = new(SwitchExperiment.Run(request.Authorization!.Request, session, () => DateTimeOffset.UtcNow,
                    () => Environment.TickCount64, () => clock.ElapsedMilliseconds, delay, qualification));
            }
            output.WriteLine(Wire.Encode(reply)); return 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { output.WriteLine(Wire.Encode(new SwitchWorkerReply(Error: "Worker rejected or failed."))); return 1; }
    }

    [SupportedOSPlatform("windows")]
    public static int Qualify(string personalText, string workText, string targetBuild = "26200.9711")
    {
        // Separate console diagnostic; never opens the app, settings or checkpoints.
        if (!Guid.TryParse(personalText, out var personal) || !Guid.TryParse(workText, out var work) ||
            personal == Guid.Empty || work == Guid.Empty || personal == work) return 2;
        try
        {
            // The build named by the explicit command must match this host. An old
            // qualification command cannot authorize a newly prepared build.
            var configuration = NativeSession.ReadConfiguration();
            if (configuration.Build != targetBuild || !SwitchCompatibility.Allowed(configuration, true)) return 2;
            var (reply, exit) = SwitchWorkerProcess.CallAsync(new("capture"), "--switch-qualification-worker").GetAwaiter().GetResult();
            if (exit != 0 || reply.Snapshot is not { } snapshot || reply.Result is not null || reply.Error is not null) return 2;
            Safety.ValidateSnapshot(snapshot, true);
            var request = new AuthorizedSwitch(new(Guid.NewGuid(), snapshot, work, true), personal, work);
            var result = IsolatedSwitchExecutor.ExecuteRequestAsync(request, true).GetAwaiter().GetResult();
            Console.WriteLine(result.Outcome + ": " + result.Message);
            return result.Outcome == SwitchOutcome.ConfirmedSuccess ? 0 : 1;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { Console.WriteLine(SwitchCompletion.Unknown("Diagnostic failed.").Message); return 1; }
    }

    // No WPF application in this worker. Pump STA messages during verification
    // waits; synchronous COM may still block and is bounded by parent termination.
    [SupportedOSPlatform("windows")]
    private static void PumpDelay(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            while (PeekMessage(out var message, 0, 0, 0, 1))
            { TranslateMessage(ref message); DispatchMessage(ref message); }
            var remaining = milliseconds - (int)timer.ElapsedMilliseconds;
            if (remaining > 0) MsgWaitForMultipleObjectsEx(0, 0, (uint)remaining, 0x04FF, 0x0004);
        } while (timer.ElapsedMilliseconds < milliseconds);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, nint window, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjectsEx(uint count, nint handles, uint timeout, uint wake, uint flags);
}
