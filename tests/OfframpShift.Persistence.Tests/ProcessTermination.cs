using System.Collections.Immutable;
using System.Diagnostics;
using OfframpShift.Core;
using OfframpShift.Persistence;
using static Program;

// Dedicated processes and disposable directories only. A stdout barrier is reached
// at the requested boundary; the parent kills that exact Process object without
// closing stdin first, so no finally/Dispose executes in the writer.
internal static class ProcessTermination
{
    internal static void Run()
    {
        foreach (var scenario in new[] { "committed", "uncommitted", "recovery-intent", "recovery-preserved", "recovery-replaced" })
            Test("ACTUAL Windows forced termination: " + scenario, () => Exercise(scenario));
    }

    private static void Exercise(string scenario)
    {
        using var f = new Fixture();
        if (scenario != "committed")
        {
            using var initial = f.Session();
            initial.Observe(f.Clock.Interval(5));
        }
        if (scenario.StartsWith("recovery-", StringComparison.Ordinal)) File.WriteAllText(f.FilePath("initialized"), "disposable unusable marker");
        var originals = scenario.StartsWith("recovery-", StringComparison.Ordinal)
            ? new[] { "checkpoint.db", "initialized" }.ToDictionary(name => name, name => File.ReadAllBytes(f.FilePath(name)))
            : null;
        if (scenario == "uncommitted") File.WriteAllText(f.FilePath("expected.json"), f.Row().Payload);
        using (var writer = Start("termination-worker", scenario, f.DirectoryPath))
        {
            try
            {
                AwaitBarrier(writer, scenario);
                using (var competing = f.Session()) Equal(StorageFailure.Busy, competing.Failure);
                if (scenario == "committed") File.WriteAllText(f.FilePath("expected.json"), f.Row().Payload);
                if (scenario == "uncommitted")
                {
                    // Prove a flushed hot rollback-journal header, not merely a
                    // managed transaction with dirty pages still only in memory.
                    using var journal = new FileStream(f.FilePath("checkpoint.db-journal"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var header = new byte[8];
                    journal.ReadExactly(header);
                    True(header.SequenceEqual(new byte[] { 0xd9, 0xd5, 0x05, 0xf9, 0x20, 0xa1, 0x63, 0xd7 }));
                    True(new FileInfo(f.FilePath("checkpoint.db")).Length > 8192);
                }
                if (originals is not null)
                {
                    True(File.Exists(f.FilePath("recovery.pending")));
                    if (scenario != "recovery-intent")
                    {
                        var archive = Directory.GetDirectories(f.DirectoryPath, "recovery-*").Single();
                        foreach (var (name, bytes) in originals) True(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(archive, name))));
                        True(File.Exists(Path.Combine(archive, "preserved")));
                    }
                    if (scenario != "recovery-replaced")
                        foreach (var (name, bytes) in originals) True(bytes.SequenceEqual(File.ReadAllBytes(f.FilePath(name))));
                }
                writer.Kill(entireProcessTree: false);
                True(writer.WaitForExit(10_000));
                True(writer.ExitCode != 0);
            }
            finally
            {
                // Cleanup targets only this newly-created child, never a PID search.
                if (!writer.HasExited) { writer.Kill(entireProcessTree: false); writer.WaitForExit(10_000); }
            }
        }
        // An entirely new process acquires the lease and validates restored state.
        using var verifier = Start("termination-verify", scenario, f.DirectoryPath);
        try
        {
            True(verifier.WaitForExit(20_000));
            var output = verifier.StandardOutput.ReadToEnd();
            var error = verifier.StandardError.ReadToEnd();
            if (verifier.ExitCode != 0) throw new InvalidOperationException("Fresh verifier failed: " + error);
            True(output.Contains("VERIFIED " + scenario, StringComparison.Ordinal));
            Console.WriteLine(output.Trim());
        }
        finally
        {
            if (!verifier.HasExited) { verifier.Kill(entireProcessTree: false); verifier.WaitForExit(10_000); }
        }
    }

    internal static int Worker(string scenario, string directory)
    {
        try
        {
            RequireDisposableDirectory(directory);
            var clock = new Clock();
            if (scenario == "committed")
            {
                using var session = DurableMonitoringSession.Open(clock, Fixture.Small with { Applications = ["excluded-opaque"] }, directory);
                session.Observe(clock.Interval(30));
                session.Snooze();
                session.Observe(clock.Interval(3));
                Equal(33L, session.State.UsageMs);
                Equal(7L, session.State.SnoozeRemainingMs);
                Barrier(scenario); // Save has returned; lease still held.
            }
            else if (scenario == "uncommitted")
            {
                using var store = new SqliteCheckpointStore(directory, () => clock.UtcNow);
                var previous = store.Load()!;
                // A payload well above SQLite's default cache forces native dirty
                // page spill; the parent independently checks the hot journal.
                var replacement = previous with
                {
                    Configuration = previous.Configuration with
                    {
                        Applications = Enumerable.Range(0, 8000).Select(i => "configured-" + i + new string('x', 1024)).ToImmutableArray()
                    },
                    State = previous.State with { UsageMs = 12 }
                };
                store.Fault = point => { if (point == StorageFault.AfterUpdate) Barrier(scenario); };
                store.Save(replacement);
                throw new InvalidOperationException("Writer must be terminated before commit.");
            }
            else
            {
                var stage = scenario switch
                {
                    "recovery-intent" => StorageFault.BeforePreservation,
                    "recovery-preserved" => StorageFault.AfterPreservation,
                    "recovery-replaced" => StorageFault.AfterReplacement,
                    _ => throw new ArgumentException("Unknown termination scenario.")
                };
                using var session = new DurableMonitoringSession(clock, Fixture.Small, directory,
                    point => { if (point == stage) Barrier(scenario); });
                Equal(PersistenceStatus.RecoveryRequired, session.Status);
                session.Reinitialize(Fixture.Small); // Explicit test-harness authorization.
                throw new InvalidOperationException("Recovery must be terminated at its barrier.");
            }
            return 1;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    internal static int Verify(string scenario, string directory)
    {
        try
        {
            RequireDisposableDirectory(directory);
            var clock = new Clock { Now = new(Guid.NewGuid(), 1_000_000) };
            clock.UtcNow = clock.UtcNow.AddDays(1);
            using var store = new SqliteCheckpointStore(directory, () => clock.UtcNow);
            if (scenario.StartsWith("recovery-", StringComparison.Ordinal))
            {
                Fails(StorageFailure.InterruptedRecovery, () => store.Load());
                store.Dispose();
                using var session = DurableMonitoringSession.Open(clock, Fixture.Small, directory);
                Equal(PersistenceStatus.RecoveryRequired, session.Status);
                Fails(StorageFailure.InterruptedRecovery, () => session.Observe(clock.Interval(1)));
                session.Reinitialize(Fixture.Small);
                Equal(PersistenceStatus.Ready, session.Status);
                Equal(0L, session.State.UsageMs);
                Equal(2, Directory.GetDirectories(directory, "recovery-*").Length);
                Console.WriteLine("VERIFIED " + scenario + ": blocked restart, archives retained, further explicit recovery succeeded.");
            }
            else
            {
                var checkpoint = store.Load()!;
                var expected = File.ReadAllText(Path.Combine(directory, "expected.json"));
                Equal(expected, CheckpointCodec.Encode(checkpoint, clock.UtcNow));
                store.Dispose();
                using var restored = DurableMonitoringSession.Open(clock, Fixture.Small, directory);
                Equal(PersistenceStatus.Ready, restored.Status);
                if (scenario == "committed")
                {
                    Equal(33L, restored.State.UsageMs);
                    Equal(InterventionState.Snoozed, restored.State.Intervention);
                    Equal(7L, restored.State.SnoozeRemainingMs);
                    Equal("excluded-opaque", restored.Configuration.Applications.Single());
                    Equal(1L, restored.State.EffectSequence);
                }
                else Equal(5L, restored.State.UsageMs);
                Equal(0L, restored.State.BreakProgressMs);
                var next = restored.Observe(clock.Interval(1));
                Equal(0, next.Effects.Length);
                Equal(scenario == "committed" ? 34L : 6L, next.State.UsageMs);
                Console.WriteLine("VERIFIED " + scenario + ": exact complete checkpoint, fresh baseline, no downtime or effect replay.");
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Barrier(string scenario)
    {
        Console.WriteLine("BARRIER " + scenario);
        Console.Out.Flush();
        Console.ReadLine();
        throw new InvalidOperationException("Barrier must end through forced termination.");
    }

    private static void AwaitBarrier(Process child, string scenario)
    {
        var line = child.StandardOutput.ReadLineAsync();
        True(line.Wait(TimeSpan.FromSeconds(30)));
        if (line.Result != "BARRIER " + scenario)
            throw new InvalidOperationException("Writer did not reach barrier: " + child.StandardError.ReadToEnd());
    }

    private static Process Start(string command, string scenario, string directory)
    {
        RequireDisposableDirectory(directory);
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(typeof(Program).Assembly.Location);
        info.ArgumentList.Add(command);
        info.ArgumentList.Add(scenario);
        info.ArgumentList.Add(directory);
        return Process.Start(info)!;
    }

    private static void RequireDisposableDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        var temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        const string prefix = "offramp-persistence-";
        var name = Path.GetFileName(full);
        if (!full.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[prefix.Length..], "N", out _) ||
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Termination checks require an owned disposable temporary directory.");
    }
}
