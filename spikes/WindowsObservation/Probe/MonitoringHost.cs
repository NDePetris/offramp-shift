using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal static class MonitoringHost
{
    public static int Run(string[] args)
    {
        if (args.Length != 9 || !int.TryParse(args[1], out var seconds) || seconds is < 1 or > 86_400 ||
            args[2] != "--desktop" || args[3] != "--personal" || !Guid.TryParse(args[4], out var personal) ||
            args[5] != "--work" || !Guid.TryParse(args[6], out var work) || args[7] != "--store" ||
            personal == Guid.Empty || work == Guid.Empty || personal == work || string.IsNullOrWhiteSpace(args[8])) return 2;
        if (!WindowsObservationSource.Supported)
        { Commands.Write(new { result = "Unsupported", accounting = "Unavailable" }); return 2; }
        var clock = new ObservationClock();
        using var session = DurableMonitoringSession.Open(clock, new() { Accounting = AccountingMode.BestEffort }, args[8]);
        if (session.Status != PersistenceStatus.Ready)
        { Commands.Write(new { persistence = session.Status.ToString(), failure = session.Failure?.ToString() }); return 1; }
        if (session.Configuration.Accounting != AccountingMode.BestEffort)
            session.ChangeConfiguration(session.Configuration with { Accounting = AccountingMode.BestEffort });
        var timer = Stopwatch.StartNew();
        using var runtime = new MonitoringRuntime(clock, clock.NewEpoch, () => timer.ElapsedMilliseconds,
            () => new WindowsObservationSource(clock), session, personal, work);
        var stop = false;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; Volatile.Write(ref stop, true); };
        Console.CancelKeyPress += cancel;
        Console.Error.WriteLine("Estimated monitoring. Ctrl+C stops. R retries the exact pending save; O retries read-only providers (10 s cooldown). No notifications or switching.");
        try
        {
            while (timer.Elapsed.TotalSeconds < seconds && !Volatile.Read(ref stop))
            {
                if (!Console.IsInputRedirected && Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.R && session.Status == PersistenceStatus.PendingCommit)
                    { try { session.RetryPendingCommit(); runtime.ResetConfiguration(); } catch (CheckpointStorageException) { } }
                    if (key == ConsoleKey.O) runtime.RetryProviders();
                }
                runtime.Step(); Thread.Sleep(10);
            }
            Commands.Write(new
            {
                accounting = "BestEffort",
                confidence = runtime.Reason == "BoundedEstimate" && runtime.Health.Healthy ? "Estimated" : "Unknown",
                persistence = session.Status.ToString(),
                failure = session.Failure?.ToString(),
                state = session.Status == PersistenceStatus.Ready ? session.State : null,
                health = runtime.Health.Healthy ? "Available" : "Unavailable",
                runtime.Reason,
                build = WindowsObservationSource.Build,
                architecture = "x64",
                elapsedMs = timer.ElapsedMilliseconds,
                diagnostics = runtime.Diagnostics,
                limitation = "Bounded estimates, not independent accounting acceptance. Final pending time is discarded."
            });
            return session.Status == PersistenceStatus.Ready && runtime.Health.Healthy ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
