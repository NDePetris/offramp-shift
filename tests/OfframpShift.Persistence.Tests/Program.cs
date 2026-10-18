using System.Diagnostics;
using System.Text.Json.Nodes;
using OfframpShift.Core;
using OfframpShift.Persistence;

internal static class Program
{
    private static int assertions;
    private static int tests;
    public static void Equal<T>(T expected, T actual)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
    internal static void True(bool value) => Equal(true, value);
    internal static void Fails(StorageFailure failure, Action action)
    {
        try { action(); }
        catch (CheckpointStorageException e) { Equal(failure, e.Failure); return; }
        throw new InvalidOperationException("Expected a typed storage failure.");
    }
    internal static void Test(string name, Action test)
    {
        test();
        tests++;
        Console.WriteLine($"PASS {name}");
    }

    private static int Main(string[] args)
    {
        if (args is ["termination-worker", var mode, var directory]) return ProcessTermination.Worker(mode, directory);
        if (args is ["termination-verify", var scenario, var storage]) return ProcessTermination.Verify(scenario, storage);
        if (args is ["lease", var path])
        {
            using var store = new SqliteCheckpointStore(path);
            Console.WriteLine("READY");
            Console.ReadLine();
            return 0;
        }
        try
        {
            Test("first launch, strict round trip and repeated full replacement", () =>
            {
                using var f = new Fixture();
                using (var store = f.Store())
                {
                    Equal<MonitoringCheckpoint?>(null, store.Load());
                    store.Save(f.Checkpoint);
                    for (var i = 1; i <= 5; i++) store.Save(f.Checkpoint with { State = f.Checkpoint.State with { UsageMs = i } });
                }
                using var reload = f.Store();
                Equal(5L, reload.Load()!.State.UsageMs);
                Equal(6L, f.Row().Revision);
                Equal(SqliteCheckpointStore.MarkerContents, File.ReadAllText(f.FilePath("initialized")));
            });
            Test("restart retains usage, configuration and pause without downtime", () =>
            {
                using var f = new Fixture();
                using (var s = f.Session())
                {
                    s.Observe(f.Clock.Interval(20));
                    s.ChangeConfiguration(Fixture.Small with { Applications = ["excluded"], AllowanceMs = 40 });
                    s.SetPaused(true);
                }
                f.Clock.Now = new(Guid.NewGuid(), 100_000);
                f.Clock.UtcNow = f.Clock.UtcNow.AddDays(10);
                using var restart = f.Session();
                Equal(20L, restart.State.UsageMs);
                Equal(40L, restart.Configuration.AllowanceMs);
                Equal("excluded", restart.Configuration.Applications[0]);
                True(restart.State.Paused);
                restart.Observe(f.Clock.Interval(500));
                Equal(20L, restart.State.UsageMs);
                restart.SetPaused(false);
                restart.Observe(f.Clock.Interval(5));
                Equal(25L, restart.State.UsageMs);
            });
            Test("completed reset and latch survive restart without intervention replay", () =>
            {
                using var f = new Fixture();
                long sequence;
                using (var s = f.Session())
                {
                    True(s.Observe(f.Clock.Interval(30)).Effects.Length > 0);
                    Equal(EffectKind.AllowanceReset, s.Observe(f.Clock.Interval(15, DesktopScope.Work)).Effects.Single().Kind);
                    sequence = s.State.EffectSequence;
                }
                using var restarted = f.Session();
                Equal(0L, restarted.State.UsageMs);
                True(restarted.State.BreakCompleted);
                Equal(sequence, restarted.State.EffectSequence);
                Equal(0, restarted.Observe(f.Clock.Interval(16, input: InputState.Idle)).Effects.Length);
                Equal(sequence, restarted.State.EffectSequence);
            });
            Test("snooze and grace suppression survive restart", () =>
            {
                using var f = new Fixture();
                using (var s = f.Session())
                {
                    s.Observe(f.Clock.Interval(30));
                    s.Snooze();
                    s.Observe(f.Clock.Interval(3));
                    Equal(7L, s.State.SnoozeRemainingMs);
                }
                f.Clock.Now = new(Guid.NewGuid(), 50_000);
                using var restart = f.Session();
                Equal(InterventionState.Snoozed, restart.State.Intervention);
                Equal(7L, restart.State.SnoozeRemainingMs);
                Equal(0, restart.Observe(f.Clock.Interval(6)).Effects.Length);
                Equal(40L, restart.Observe(f.Clock.Interval(1)).State.UsageMs);
            });
            Test("partial break, idle qualification and invalidation are written", () =>
            {
                using var f = new Fixture();
                using (var s = f.Session())
                {
                    s.Observe(f.Clock.Interval(3, DesktopScope.Work, InputState.Idle));
                    Equal(3L, Decode(f).State.BreakProgressMs);
                    Equal(3L, Decode(f).State.IdleProgressMs);
                    s.Observe(f.Clock.Interval(1, confidence: EvidenceConfidence.Unknown));
                    Equal(0L, Decode(f).State.BreakProgressMs);
                    Equal(0L, Decode(f).State.IdleProgressMs);
                    s.Observe(f.Clock.Interval(4, DesktopScope.Work));
                }
                using var restart = f.Session();
                Equal(0L, restart.State.BreakProgressMs);
                Equal(0L, restart.State.IdleProgressMs);
            });
            Test("no-op transitions do not write", () =>
            {
                using var f = new Fixture();
                using var s = f.Session();
                var row = f.Row();
                s.SetPaused(false);
                s.Snooze();
                s.Observe(f.Clock.Interval(1, confidence: EvidenceConfidence.Unknown));
                Equal(row, f.Row());
                var observation = f.Clock.Interval(1);
                s.Observe(observation);
                row = f.Row();
                s.Observe(observation);
                Equal(row, f.Row());
            });
            Test("strict codec rejects every missing, duplicate, null and incorrect field", StrictCodec);
            Test("invalid domain states and versions never restore", InvalidDomains);
            Test("missing initialized database and checkpoint require recovery", () =>
            {
                using var f = new Fixture();
                f.Initialize();
                File.Delete(f.FilePath("checkpoint.db"));
                using (var s = f.Session()) Equal(PersistenceStatus.RecoveryRequired, s.Status);
                Equal(SqliteCheckpointStore.MarkerContents, File.ReadAllText(f.FilePath("initialized")));
                using var other = new Fixture();
                other.Initialize();
                other.Sql("DELETE FROM checkpoint");
                using var missingRow = other.Session();
                Equal(StorageFailure.MissingEstablishedState, missingRow.Failure);
            });
            Test("orphaned database, malformed marker and truncated database retained", () =>
            {
                foreach (var variant in new[] { "marker", "orphan", "truncated", "random" })
                {
                    using var f = new Fixture();
                    f.Initialize();
                    if (variant == "marker") File.WriteAllText(f.FilePath("initialized"), "bad");
                    if (variant == "orphan") File.Delete(f.FilePath("initialized"));
                    if (variant == "truncated") using (var file = File.OpenWrite(f.FilePath("checkpoint.db"))) file.SetLength(100);
                    if (variant == "random") File.WriteAllText(f.FilePath("checkpoint.db"), "garbage");
                    var original = File.ReadAllBytes(f.FilePath("checkpoint.db"));
                    using var s = f.Session();
                    Equal(PersistenceStatus.RecoveryRequired, s.Status);
                    True(original.SequenceEqual(File.ReadAllBytes(f.FilePath("checkpoint.db"))));
                }
            });
            Test("digest mismatch, identity and schema corruption block", () =>
            {
                foreach (var sql in new[] { "UPDATE checkpoint SET digest=printf('%064d',0)", "PRAGMA application_id=1", "CREATE TABLE history(value TEXT)", "PRAGMA user_version=0" })
                {
                    using var f = new Fixture();
                    f.Initialize();
                    f.Sql(sql);
                    using var s = f.Session();
                    Equal(StorageFailure.Corruption, s.Failure);
                }
            });
            Test("unsupported storage and domain versions block without writes", () =>
            {
                foreach (var domain in new[] { false, true })
                {
                    using var f = new Fixture();
                    f.Initialize();
                    if (domain) f.Payload(f.Row().Payload.Replace("\"Version\":3", "\"Version\":4", StringComparison.Ordinal));
                    else f.Sql("PRAGMA user_version=2");
                    var bytes = File.ReadAllBytes(f.FilePath("checkpoint.db"));
                    using var s = f.Session();
                    Equal(PersistenceStatus.Incompatible, s.Status);
                    Fails(StorageFailure.UnsupportedVersion, () => s.SetPaused(true));
                    True(bytes.SequenceEqual(File.ReadAllBytes(f.FilePath("checkpoint.db"))));
                }
            });
            Test("unavailable and read-only storage block", AccessFailures);
            Test("persistent exclusive lease excludes same and competing processes", Lease);
            Test("SQLite lock timeout is bounded and explicit", () =>
            {
                using var f = new Fixture();
                f.Initialize();
                using var store = f.Store();
                var cp = store.Load()!;
                using var blocker = f.Connection();
                using var transaction = blocker.BeginTransaction(deferred: false);
                var timer = Stopwatch.StartNew();
                Fails(StorageFailure.Busy, () => store.Save(cp));
                True(timer.Elapsed.TotalSeconds < 5);
                Equal(1L, f.Row().Revision);
            });
            Test("stale revision and same-revision conflicts never overwrite", () =>
            {
                foreach (var revision in new[] { 1L, 2L })
                {
                    using var f = new Fixture();
                    f.Initialize();
                    using var store = f.Store();
                    var checkpoint = store.Load()!;
                    f.Payload(CheckpointCodec.Encode(checkpoint with { State = checkpoint.State with { UsageMs = 7 } }, f.Clock.UtcNow), revision);
                    var row = f.Row();
                    Fails(StorageFailure.StaleRevision, () => store.Save(checkpoint));
                    Equal(row, f.Row());
                }
            });
            foreach (var fault in new[] { StorageFault.BeforeUpdate, StorageFault.AfterUpdate, StorageFault.AfterCommit })
            {
                Test($"real transaction {fault}: previous/new complete state and exact session retry", () => Interruption(fault));
                Test($"restart after {fault}: no baseline or effects replay", () => RestartAfterFault(fault));
                Test($"interrupted initial transaction {fault}: never fresh fallback", () =>
                {
                    using var f = new Fixture();
                    using (var s = f.Session(stage => { if (stage == fault) throw new IOException(); }))
                        True(s.Status != PersistenceStatus.Ready);
                    using var restart = f.Session();
                    Equal(fault == StorageFault.AfterCommit ? PersistenceStatus.Ready : PersistenceStatus.RecoveryRequired, restart.Status);
                });
            }
            Test("uncertain retry rejects conflicting replacement", () =>
            {
                using var f = new Fixture();
                var active = false;
                using var s = f.Session(stage => { if (active && stage == StorageFault.AfterCommit) throw new IOException(); });
                active = true;
                Fails(StorageFailure.UncertainCommit, () => s.Observe(f.Clock.Interval(30)));
                var cp = Decode(f);
                f.Payload(CheckpointCodec.Encode(cp with { State = cp.State with { UsageMs = 50 } }, f.Clock.UtcNow), 2);
                var conflict = f.Row();
                Fails(StorageFailure.StaleRevision, s.RetryPendingCommit);
                Equal(PersistenceStatus.Blocked, s.Status);
                Equal(conflict, f.Row());
            });
            Test("explicit recovery preserves marker, database and companions under Windows lease", Recovery);
            foreach (var stage in new[] { StorageFault.BeforePreservation, StorageFault.AfterPreservation, StorageFault.AfterReplacement })
                Test($"interrupted recovery {stage} remains detected on restart", () => RecoveryFault(stage));
            Test("failed preservation leaves original bytes and blocks replacement", PreservationFailure);
            Test("future versions remain incompatible during explicit recovery", () =>
            {
                foreach (var domain in new[] { false, true })
                {
                    using var f = new Fixture();
                    f.Initialize();
                    File.WriteAllText(f.FilePath("initialized"), "bad");
                    using var session = f.Session();
                    Equal(PersistenceStatus.RecoveryRequired, session.Status);
                    if (domain) f.Payload("{\"Version\":4,\"FutureLayout\":true}");
                    else f.Sql("PRAGMA user_version=2");
                    var original = File.ReadAllBytes(f.FilePath("checkpoint.db"));
                    Fails(StorageFailure.UnsupportedVersion, () => session.Reinitialize(Fixture.Small));
                    Equal(PersistenceStatus.Incompatible, session.Status);
                    True(original.SequenceEqual(File.ReadAllBytes(f.FilePath("checkpoint.db"))));
                    Equal(0, Directory.GetDirectories(f.DirectoryPath, "recovery-*").Length);
                }
            });
            Test("preserved recovery artifacts alone never imply first launch", () =>
            {
                using var f = new Fixture();
                Directory.CreateDirectory(f.FilePath("recovery-existing"));
                using var session = f.Session();
                Equal(StorageFailure.InterruptedRecovery, session.Failure);
                True(!File.Exists(f.FilePath("checkpoint.db")));
            });
            Test("marker access failure cannot be interpreted as absence", () =>
            {
                using var f = new Fixture();
                f.Initialize();
                var row = f.Row();
                using var locked = new FileStream(f.FilePath("initialized"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var session = f.Session();
                Equal(PersistenceStatus.Blocked, session.Status);
                Equal(StorageFailure.AccessFailure, session.Failure);
                Equal(row, f.Row());
            });
            Test("retry discards partial continuity and rejects old monotonic evidence", () =>
            {
                using var f = new Fixture();
                var active = false;
                using var session = f.Session(stage => { if (active && stage == StorageFault.AfterCommit) throw new IOException(); });
                session.Observe(f.Clock.Interval(5));
                active = true;
                var old = f.Clock.Interval(3, DesktopScope.Work, InputState.Idle);
                Fails(StorageFailure.UncertainCommit, () => session.Observe(old));
                Equal(3L, Decode(f).State.BreakProgressMs);
                active = false;
                f.Clock.Now = new(Guid.NewGuid(), 100_000);
                session.RetryPendingCommit();
                Equal(0L, session.State.BreakProgressMs);
                Equal(0L, session.State.IdleProgressMs);
                Equal(5L, session.State.UsageMs);
                Equal(0, session.Observe(old).Effects.Length);
                Equal(5L, session.State.UsageMs);
                session.Observe(f.Clock.Interval(1));
                Equal(6L, session.State.UsageMs);
            });
            Test("concurrent session commands serialize complete commits", () =>
            {
                using var f = new Fixture();
                using var session = f.Session();
                Parallel.For(0, 16, i => session.ChangeConfiguration(Fixture.Small with { AllowanceMs = 40 + i }));
                Equal(17L, f.Row().Revision);
                Equal(session.Configuration.AllowanceMs, Decode(f).Configuration.AllowanceMs);
                Equal(PersistenceStatus.Ready, session.Status);
            });
            Test("unusable storage during pending retry permits only explicit recovery", () =>
            {
                using var f = new Fixture();
                var active = false;
                using var session = f.Session(stage => { if (active && stage == StorageFault.BeforeUpdate) throw new IOException(); });
                active = true;
                Fails(StorageFailure.AccessFailure, () => session.Observe(f.Clock.Interval(30)));
                File.Delete(f.FilePath("checkpoint.db"));
                active = false;
                Fails(StorageFailure.MissingEstablishedState, session.RetryPendingCommit);
                Equal(PersistenceStatus.RecoveryRequired, session.Status);
                session.Reinitialize(Fixture.Small);
                Equal(PersistenceStatus.Ready, session.Status);
                Equal(0L, session.State.UsageMs);
            });
            Test("serialization excludes observations and native identities", () =>
            {
                using var f = new Fixture();
                using var s = f.Session();
                s.Observe(f.Clock.Interval(5));
                var payload = f.Row().Payload;
                True(!payload.Contains("opaque-app", StringComparison.Ordinal));
                True(!payload.Contains(f.Clock.Now.Epoch.ToString(), StringComparison.Ordinal));
                Equal(4, JsonNode.Parse(payload)!.AsObject().Count);
            });
            ReviewRegressions.Run();
            ResetUsagePersistence.Run();
            BestEffortPersistence.Run();
            InteractivePersistence.Run();
            if (OperatingSystem.IsWindows()) ProcessTermination.Run();
            else Console.WriteLine("Windows process-termination checks NOT RUN on this platform.");
            Console.WriteLine($"Persistence: {tests} tests, {assertions} assertions passed (real local SQLite; exception injection and forced termination reported separately).");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static MonitoringCheckpoint Decode(Fixture f) => CheckpointCodec.Decode(f.Row().Payload, f.Clock.UtcNow);

    private static void StrictCodec()
    {
        using var f = new Fixture();
        var payload = CheckpointCodec.Encode(f.Checkpoint, f.Clock.UtcNow);
        Equal(payload, CheckpointCodec.Encode(CheckpointCodec.Decode(payload, f.Clock.UtcNow), f.Clock.UtcNow));
        foreach (var parent in new[] { "", "Configuration", "State" })
        {
            var original = JsonNode.Parse(payload)!;
            var obj = parent == "" ? original.AsObject() : original[parent]!.AsObject();
            foreach (var name in obj.Select(x => x.Key).ToArray())
            {
                var missing = JsonNode.Parse(payload)!;
                (parent == "" ? missing.AsObject() : missing[parent]!.AsObject()).Remove(name);
                Fails(StorageFailure.InvalidPayload, () => CheckpointCodec.Decode(missing.ToJsonString(), f.Clock.UtcNow));
                foreach (var wrong in new[] { "null", "\"wrong\"", "[]", "{}" })
                {
                    if (name == "Applications" && wrong == "[]") continue;
                    var changed = JsonNode.Parse(payload)!;
                    (parent == "" ? changed.AsObject() : changed[parent]!.AsObject())[name] = JsonNode.Parse(wrong);
                    Fails(StorageFailure.InvalidPayload, () => CheckpointCodec.Decode(changed.ToJsonString(), f.Clock.UtcNow));
                }
                var token = "\"" + name + "\":";
                var duplicate = payload.Replace(token, token + obj[name]!.ToJsonString() + "," + token, StringComparison.Ordinal);
                Fails(StorageFailure.InvalidPayload, () => CheckpointCodec.Decode(duplicate, f.Clock.UtcNow));
            }
        }
        foreach (var malformed in new[] { "{", "null", "[]", "{}", payload.Replace("\"UsageMs\":0", "\"UsageMs\":0.1", StringComparison.Ordinal), payload.Insert(1, "\"Unexpected\":0,") })
            Fails(StorageFailure.InvalidPayload, () => CheckpointCodec.Decode(malformed, f.Clock.UtcNow));
    }

    private static void InvalidDomains()
    {
        using var f = new Fixture();
        var cp = f.Checkpoint;
        foreach (var invalid in new[]
        {
            cp with { SavedUtc = default }, cp with { SavedUtc = f.Clock.UtcNow.AddSeconds(1) },
            cp with { State = cp.State with { UsageMs = -1 } }, cp with { State = cp.State with { UsageMs = 30 } },
            cp with { State = cp.State with { Intervention = (InterventionState)99 } },
            cp with { State = cp.State with { Recovery = RecoveryStatus.Required } },
            cp with { State = cp.State with { Paused = true, BreakProgressMs = 1 } },
            cp with { State = cp.State with { BreakCompleted = true } },
            cp with { State = cp.State with { SnoozeRemainingMs = 1 } },
            cp with { Configuration = cp.Configuration with { Applications = ["duplicate", "duplicate"] } },
            cp with { Configuration = cp.Configuration with { AllowanceMs = 0 } }
        })
        {
            var category = invalid.SavedUtc > f.Clock.UtcNow && invalid.IsValid(invalid.SavedUtc)
                ? StorageFailure.ClockUnavailable : StorageFailure.InvalidPayload;
            Fails(category, () => CheckpointCodec.Encode(invalid, f.Clock.UtcNow));
            var serialized = System.Text.Json.JsonSerializer.Serialize(invalid);
            Fails(category, () => CheckpointCodec.Decode(serialized, f.Clock.UtcNow));
        }
        f.Initialize();
        foreach (var bad in new[] { "{", "{}", System.Text.Json.JsonSerializer.Serialize(cp with { State = cp.State with { UsageMs = -1 } }) })
        {
            f.Payload(bad);
            using var s = f.Session();
            Equal(PersistenceStatus.RecoveryRequired, s.Status);
            Fails(StorageFailure.InvalidPayload, () => s.Observe(f.Clock.Interval(1)));
        }
    }

    private static void Interruption(StorageFault fault)
    {
        using var f = new Fixture();
        var active = false;
        using var s = f.Session(stage => { if (active && stage == fault) throw new IOException("private detail"); });
        var old = f.Row();
        var expectedPayload = CheckpointCodec.Encode(f.Checkpoint with
        {
            State = f.Checkpoint.State with
            {
                UsageMs = 30,
                Exhausted = true,
                Intervention = InterventionState.Grace,
                ReminderRemainingMs = 6,
                EffectSequence = 1
            }
        }, f.Clock.UtcNow);
        active = true;
        var failure = fault == StorageFault.AfterCommit ? StorageFailure.UncertainCommit : StorageFailure.AccessFailure;
        Fails(failure, () => s.Observe(f.Clock.Interval(30)));
        Equal(PersistenceStatus.PendingCommit, s.Status);
        var row = f.Row();
        Equal(fault == StorageFault.AfterCommit ? 30L : 0L, Decode(f).State.UsageMs);
        if (fault != StorageFault.AfterCommit) Equal(old, row);
        else Equal(expectedPayload, row.Payload);
        Fails(failure, () => s.Observe(f.Clock.Interval(5)));
        Fails(failure, () => s.SetPaused(true));
        Fails(failure, () => s.Snooze());
        Fails(failure, () => s.ChangeConfiguration(Fixture.Small));
        Fails(failure, () => _ = s.State);
        Equal(row, f.Row());
        active = false;
        f.Clock.UtcNow = f.Clock.UtcNow.AddHours(1);
        f.Clock.Now = new(Guid.NewGuid(), 500);
        s.RetryPendingCommit();
        Equal(PersistenceStatus.Ready, s.Status);
        Equal(30L, s.State.UsageMs);
        Equal(2L, f.Row().Revision);
        Equal(expectedPayload, f.Row().Payload);
        Equal(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), Decode(f).SavedUtc);
        s.Observe(f.Clock.Interval(2));
        Equal(32L, s.State.UsageMs);
        Equal(0L, s.State.BreakProgressMs);
    }

    private static void RestartAfterFault(StorageFault fault)
    {
        using var f = new Fixture();
        var active = false;
        using (var s = f.Session(stage => { if (active && stage == fault) throw new IOException(); }))
        {
            active = true;
            Fails(fault == StorageFault.AfterCommit ? StorageFailure.UncertainCommit : StorageFailure.AccessFailure,
                () => s.Observe(f.Clock.Interval(30)));
        }
        f.Clock.Now = new(Guid.NewGuid(), 1_000_000);
        using var restart = f.Session();
        Equal(fault == StorageFault.AfterCommit ? 30L : 0L, restart.State.UsageMs);
        var transition = restart.Observe(f.Clock.Interval(1));
        Equal(0, transition.Effects.Length);
        Equal(fault == StorageFault.AfterCommit ? 31L : 1L, transition.State.UsageMs);
    }

    private static void AccessFailures()
    {
        using var f = new Fixture();
        var blocker = f.FilePath("file");
        File.WriteAllText(blocker, "file");
        using (var session = DurableMonitoringSession.Open(f.Clock, Fixture.Small, blocker))
            Equal(PersistenceStatus.Blocked, session.Status);
        f.Initialize();
        File.SetAttributes(f.FilePath("checkpoint.db"), FileAttributes.ReadOnly);
        try
        {
            using var session = f.Session();
            // Loading a read-only file can succeed; monitoring must block on its first write.
            if (session.Status == PersistenceStatus.Ready)
            {
                Fails(StorageFailure.AccessFailure, () => session.Observe(f.Clock.Interval(1)));
                Equal(PersistenceStatus.PendingCommit, session.Status);
                Equal(0L, Decode(f).State.UsageMs);
            }
            else Equal(PersistenceStatus.Blocked, session.Status);
        }
        finally { File.SetAttributes(f.FilePath("checkpoint.db"), FileAttributes.Normal); }
    }

    private static void Lease()
    {
        using var f = new Fixture();
        using (var first = f.Session())
        using (var other = f.Session()) Equal(StorageFailure.Busy, other.Failure);
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(typeof(Program).Assembly.Location);
        info.ArgumentList.Add("lease");
        info.ArgumentList.Add(f.DirectoryPath);
        using var child = Process.Start(info)!;
        try
        {
            var line = child.StandardOutput.ReadLineAsync();
            True(line.Wait(TimeSpan.FromSeconds(10)));
            Equal("READY", line.Result);
            using var blocked = f.Session();
            Equal(PersistenceStatus.Blocked, blocked.Status);
            Equal(StorageFailure.Busy, blocked.Failure);
        }
        finally
        {
            child.StandardInput.WriteLine("stop");
            if (!child.WaitForExit(10_000)) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
        }
        Equal(0, child.ExitCode);
        True(File.Exists(f.FilePath("owner.lock")));
        using var restart = f.Session();
        Equal(PersistenceStatus.Ready, restart.Status);
    }

    private static void Recovery()
    {
        using var f = new Fixture();
        f.Initialize();
        File.WriteAllText(f.FilePath("initialized"), "damaged-marker");
        File.WriteAllText(f.FilePath("checkpoint.db-journal"), "companion");
        var originals = new[] { "checkpoint.db", "initialized", "checkpoint.db-journal" }.ToDictionary(name => name, name => File.ReadAllBytes(f.FilePath(name)));
        using (var session = f.Session())
        {
            Equal(PersistenceStatus.RecoveryRequired, session.Status);
            session.Reinitialize(Fixture.Small with { AllowanceMs = 60 });
            Equal(PersistenceStatus.Ready, session.Status);
            Equal(0L, session.State.UsageMs);
            Equal(60L, session.Configuration.AllowanceMs);
            using var blocked = f.Session();
            Equal(StorageFailure.Busy, blocked.Failure);
        }
        var archive = Directory.GetDirectories(f.DirectoryPath, "recovery-*").Single();
        foreach (var (name, bytes) in originals) True(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(archive, name))));
        True(File.Exists(Path.Combine(archive, "preserved")));
        True(!File.Exists(f.FilePath("recovery.pending")));
        using var restart = f.Session();
        Equal(60L, restart.Configuration.AllowanceMs);
    }

    private static void RecoveryFault(StorageFault fault)
    {
        using var f = new Fixture();
        f.Initialize();
        File.WriteAllText(f.FilePath("initialized"), "bad");
        using (var session = f.Session(stage => { if (stage == fault) throw new IOException(); }))
        {
            Fails(StorageFailure.AccessFailure, () => session.Reinitialize(Fixture.Small));
            Equal(PersistenceStatus.RecoveryRequired, session.Status);
        }
        using (var restart = f.Session())
        {
            Equal(StorageFailure.InterruptedRecovery, restart.Failure);
            Fails(StorageFailure.InterruptedRecovery, () => restart.SetPaused(false));
            restart.Reinitialize(Fixture.Small);
            Equal(PersistenceStatus.Ready, restart.Status);
        }
        Equal(2, Directory.GetDirectories(f.DirectoryPath, "recovery-*").Length);
        using var successful = f.Session();
        Equal(PersistenceStatus.Ready, successful.Status);
    }

    private static void PreservationFailure()
    {
        using var f = new Fixture();
        f.Initialize();
        File.WriteAllText(f.FilePath("initialized"), "bad");
        var bytes = File.ReadAllBytes(f.FilePath("checkpoint.db"));
        using var session = f.Session();
        using (var locked = new FileStream(f.FilePath("initialized"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Fails(StorageFailure.AccessFailure, () => session.Reinitialize(Fixture.Small));
        Equal("bad", File.ReadAllText(f.FilePath("initialized")));
        True(bytes.SequenceEqual(File.ReadAllBytes(f.FilePath("checkpoint.db"))));
        Equal(PersistenceStatus.RecoveryRequired, session.Status);
        True(File.Exists(f.FilePath("recovery.pending")));
    }
}
