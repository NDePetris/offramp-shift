using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OfframpShift.Core;

namespace OfframpShift.Persistence;

public sealed class SqliteCheckpointStore : ICheckpointStore, IDisposable
{
    internal const string DatabaseName = "checkpoint.db";
    internal const string MarkerName = "initialized";
    internal const string RecoveryName = "recovery.pending";
    internal const string MarkerContents = "Offramp Shift checkpoint storage 1\n";
    internal const int ApplicationId = 0x4f535346;
    internal const string Schema = "CREATE TABLE checkpoint (id INTEGER PRIMARY KEY CHECK(id = 1), revision INTEGER NOT NULL CHECK(revision > 0), payload TEXT NOT NULL, digest TEXT NOT NULL CHECK(length(digest) = 64)) STRICT";
    private static readonly string[] Artifacts = [DatabaseName, DatabaseName + "-journal", DatabaseName + "-wal", DatabaseName + "-shm", MarkerName, RecoveryName];
    private readonly object gate = new();
    private readonly string directory;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly FileStream lease;
    private long? expectedRevision;
    private string? committedPayload;
    private string? pendingPayload;
    private bool fresh;
    private bool disposed;
    internal Action<StorageFault>? Fault { get; set; }
    public long PreparedPayloadBytes { get; private set; }
    private readonly LatencyHistogram steadyState = new(), setup = new(), begin = new(), validation = new(), replacement = new(), commit = new(), disposal = new();
    // Serialized by the durable session. Initialization/recovery are excluded.
    internal object SaveDiagnostics => new
    {
        steadyState = steadyState.Snapshot(),
        connectionSetup = setup.Snapshot(),
        transactionBegin = begin.Snapshot(),
        existingStateValidation = validation.Snapshot(),
        replacement = replacement.Snapshot(),
        commit = commit.Snapshot(),
        disposal = disposal.Snapshot()
    };

    private readonly struct TimedDisposal(IDisposable resource, LatencyHistogram latency) : IDisposable
    {
        public void Dispose() { using var measurement = latency.Measure(); resource.Dispose(); }
    }

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Offramp", "Shift");

    public SqliteCheckpointStore(string directory, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        try
        {
            Directory.CreateDirectory(this.directory);
            // This persistent file is only a lease, not an initialization sentinel.
            // It is never moved/deleted; the marker remains independently readable.
            lease = new FileStream(PathFor("owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33)
        {
            throw new CheckpointStorageException(StorageFailure.Busy);
        }
        catch (Exception e) when (IsStorageError(e)) { throw Map(e); }
    }

    public MonitoringCheckpoint? Load()
    {
        lock (gate)
        {
            ThrowDisposed();
            try
            {
                CheckRecovery();
                var existing = Artifacts.Where(name => Present(PathFor(name))).ToArray();
                if (existing.Length == 0 && expectedRevision is null)
                {
                    if (Directory.EnumerateDirectories(directory, "recovery-*").Any())
                        throw new CheckpointStorageException(StorageFailure.InterruptedRecovery);
                    fresh = true;
                    expectedRevision = 0;
                    return null;
                }
                CheckMarker();
                if (!Present(PathFor(DatabaseName))) throw new CheckpointStorageException(StorageFailure.MissingEstablishedState);
                using var connection = Open(false);
                using var transaction = connection.BeginTransaction(deferred: false);
                var row = Read(connection, transaction);
                if (expectedRevision is not null && (row.Revision != expectedRevision || row.Payload != committedPayload))
                    throw new CheckpointStorageException(StorageFailure.StaleRevision);
                var checkpoint = CheckpointCodec.Decode(row.Payload, utcNow());
                CheckWritable();
                transaction.Commit();
                expectedRevision = row.Revision;
                committedPayload = row.Payload;
                return checkpoint;
            }
            catch (Exception e) when (IsStorageError(e)) { throw Map(e); }
        }
    }

    public void Save(MonitoringCheckpoint checkpoint)
    {
        lock (gate)
        {
            ThrowDisposed();
            var payload = CheckpointCodec.Encode(checkpoint, utcNow());
            var bytes = Encoding.UTF8.GetByteCount(payload);
            PreparedPayloadBytes = bytes > long.MaxValue - PreparedPayloadBytes ? long.MaxValue : PreparedPayloadBytes + bytes;
            if (expectedRevision is null) throw new CheckpointStorageException(StorageFailure.MissingEstablishedState);
            if (pendingPayload is not null && pendingPayload != payload) throw new CheckpointStorageException(StorageFailure.StaleRevision);
            var commitStarted = false;
            try
            {
                CheckRecovery();
                if (fresh)
                {
                    // First-run permission is consumed before the first filesystem mutation.
                    fresh = false;
                    WriteFlushed(PathFor(MarkerName), MarkerContents, FileMode.CreateNew);
                    Initialize(payload);
                    return;
                }
                using var steadyMeasurement = steadyState.Measure();
                CheckMarker();
                if (!Present(PathFor(DatabaseName))) throw new CheckpointStorageException(StorageFailure.MissingEstablishedState);
                long revision;
                {
                    SqliteConnection connection;
                    using (setup.Measure()) connection = Open(false);
                    using var closeConnection = new TimedDisposal(connection, disposal);
                    SqliteTransaction transaction;
                    using (begin.Measure()) transaction = connection.BeginTransaction(deferred: false);
                    using var closeTransaction = new TimedDisposal(transaction, disposal);
                    (long Revision, string Payload) row;
                    bool alreadyCommitted;
                    using (validation.Measure())
                    {
                        row = Read(connection, transaction);
                        alreadyCommitted = pendingPayload is not null && expectedRevision < long.MaxValue && row.Revision == expectedRevision + 1 && row.Payload == pendingPayload;
                        if (!alreadyCommitted)
                        {
                            if (row.Revision != expectedRevision || row.Payload != committedPayload || row.Revision == long.MaxValue)
                                throw new CheckpointStorageException(StorageFailure.StaleRevision);
                            CheckpointCodec.Decode(row.Payload, utcNow());
                        }
                    }
                    if (alreadyCommitted)
                    {
                        // The acknowledgment was lost, but the exact replacement committed.
                        revision = row.Revision;
                        using (commit.Measure()) transaction.Commit();
                    }
                    else
                    {
                        revision = checked(row.Revision + 1);
                        pendingPayload = payload;
                        Fault?.Invoke(StorageFault.BeforeUpdate);
                        using (replacement.Measure()) Replace(connection, transaction, revision, payload, false);
                        Fault?.Invoke(StorageFault.AfterUpdate);
                        commitStarted = true;
                        using (commit.Measure()) transaction.Commit();
                        Fault?.Invoke(StorageFault.AfterCommit);
                    }
                }
                // Include connection/transaction disposal in the acknowledgment boundary.
                expectedRevision = revision;
                committedPayload = payload;
                pendingPayload = null;
            }
            catch (Exception e) when (e is not CheckpointStorageException)
            {
                // Once COMMIT is attempted, conservatively retain exact retry identity.
                if (commitStarted)
                {
                    pendingPayload = payload;
                    throw new CheckpointStorageException(StorageFailure.UncertainCommit);
                }
                throw IsStorageError(e) ? Map(e) : new CheckpointStorageException(StorageFailure.UnexpectedFailure);
            }
        }
    }

    // Called only by explicit session recovery. No connection survives an operation.
    internal void Reinitialize(MonitoringCheckpoint checkpoint)
    {
        lock (gate)
        {
            ThrowDisposed();
            var payload = CheckpointCodec.Encode(checkpoint, utcNow());
            try
            {
                var requiresJournalValidation = EnsureRecoveryCompatible(directory);
                var archiveName = "recovery-" + Guid.NewGuid().ToString("N");
                // Intent is flushed before any original is removed. A previous intent
                // is itself preserved, never interpreted as permission to resume.
                var originals = Artifacts.Where(name => Present(PathFor(name))).ToArray();
                var archive = PathFor(archiveName);
                // Preserve a previous recovery intent before replacing it. Establish
                // the new intent before even the preservation phase can be interrupted.
                if (originals.Contains(RecoveryName))
                {
                    Directory.CreateDirectory(archive);
                    CopyFlushed(PathFor(RecoveryName), Path.Combine(archive, RecoveryName));
                }
                WriteFlushed(PathFor(RecoveryName), archiveName + "\n", FileMode.Create);
                Directory.CreateDirectory(archive);
                Fault?.Invoke(StorageFault.BeforePreservation);
                foreach (var name in originals.Where(name => name != RecoveryName))
                {
                    CopyFlushed(PathFor(name), Path.Combine(archive, name));
                }
                WriteFlushed(Path.Combine(archive, "preserved"), "Original artifacts preserved.\n", FileMode.CreateNew);
                if (requiresJournalValidation)
                {
                    // Recover only a disposable copy. Archives remain byte-for-byte
                    // originals, and future domain versions hidden by a hot journal
                    // cannot bypass the compatibility guard.
                    var validation = Path.Combine(archive, "journal-validation");
                    Directory.CreateDirectory(validation);
                    foreach (var name in originals.Where(name => name.StartsWith(DatabaseName, StringComparison.Ordinal)))
                        CopyFlushed(Path.Combine(archive, name), Path.Combine(validation, name));
                    EnsureRecoveryCompatible(validation, recoverJournal: true);
                }
                Fault?.Invoke(StorageFault.AfterPreservation);
                foreach (var name in originals.Where(name => name != RecoveryName)) File.Delete(PathFor(name));
                expectedRevision = 0;
                committedPayload = pendingPayload = null;
                fresh = false;
                WriteFlushed(PathFor(MarkerName), MarkerContents, FileMode.CreateNew);
                Initialize(payload);
                Fault?.Invoke(StorageFault.AfterReplacement);
                File.Delete(PathFor(RecoveryName));
            }
            catch (Exception e) when (IsStorageError(e)) { throw Map(e); }
        }
    }

    private void Initialize(string payload)
    {
        // Refuse accidental replacement even if the marker survived interrupted init.
        using (new FileStream(PathFor(DatabaseName), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        var commitStarted = false;
        try
        {
            using var connection = Open(true);
            using var transaction = connection.BeginTransaction(deferred: false);
            Execute(connection, transaction, $"PRAGMA application_id={ApplicationId}; PRAGMA user_version=1; {Schema};");
            Fault?.Invoke(StorageFault.BeforeUpdate);
            Replace(connection, transaction, 1, payload, true);
            Fault?.Invoke(StorageFault.AfterUpdate);
            commitStarted = true;
            transaction.Commit();
            Fault?.Invoke(StorageFault.AfterCommit);
        }
        catch (Exception e) when (e is not CheckpointStorageException)
        {
            if (commitStarted) throw new CheckpointStorageException(StorageFailure.UncertainCommit);
            throw IsStorageError(e) ? Map(e) : new CheckpointStorageException(StorageFailure.UnexpectedFailure);
        }
        expectedRevision = 1;
        committedPayload = payload;
        pendingPayload = null;
    }

    private SqliteConnection Open(bool initialize)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = PathFor(DatabaseName),
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 1
        }.ToString());
        try
        {
            connection.Open();
            if (!initialize)
            {
                var version = Convert.ToInt64(Scalar(connection, null, "PRAGMA user_version"), CultureInfo.InvariantCulture);
                if (version != 1) throw new CheckpointStorageException(version == 0 ? StorageFailure.Corruption : StorageFailure.UnsupportedVersion);
                if (Convert.ToInt64(Scalar(connection, null, "PRAGMA application_id"), CultureInfo.InvariantCulture) != ApplicationId)
                    throw new CheckpointStorageException(StorageFailure.Corruption);
            }
            var mode = Scalar(connection, null, initialize ? "PRAGMA journal_mode=DELETE" : "PRAGMA journal_mode");
            if (!string.Equals(mode as string, "delete", StringComparison.OrdinalIgnoreCase)) throw new CheckpointStorageException(StorageFailure.Corruption);
            Execute(connection, null, "PRAGMA synchronous=EXTRA");
            if (Convert.ToInt64(Scalar(connection, null, "PRAGMA synchronous"), CultureInfo.InvariantCulture) != 3)
                throw new CheckpointStorageException(StorageFailure.AccessFailure);
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static (long Revision, string Payload) Read(SqliteConnection connection, SqliteTransaction transaction)
    {
        var version = Convert.ToInt64(Scalar(connection, transaction, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version != 1) throw new CheckpointStorageException(version == 0 ? StorageFailure.Corruption : StorageFailure.UnsupportedVersion);
        if (Convert.ToInt64(Scalar(connection, transaction, "PRAGMA application_id"), CultureInfo.InvariantCulture) != ApplicationId)
            throw new CheckpointStorageException(StorageFailure.Corruption);
        if (!Equals(Scalar(connection, transaction, "PRAGMA integrity_check"), "ok")) throw new CheckpointStorageException(StorageFailure.Corruption);
        using (var schema = Command(connection, transaction, "SELECT sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY name"))
        using (var reader = schema.ExecuteReader())
            if (!reader.Read() || reader.GetString(0) != Schema || reader.Read()) throw new CheckpointStorageException(StorageFailure.Corruption);
        using var command = Command(connection, transaction, "SELECT id, revision, payload, digest FROM checkpoint");
        using var rows = command.ExecuteReader();
        if (!rows.Read()) throw new CheckpointStorageException(StorageFailure.MissingEstablishedState);
        var id = rows.GetInt64(0);
        var revision = rows.GetInt64(1);
        var payload = rows.GetString(2);
        var digest = rows.GetString(3);
        if (id != 1 || revision <= 0 || digest != Digest(revision, payload) || rows.Read()) throw new CheckpointStorageException(StorageFailure.Corruption);
        return (revision, payload);
    }

    internal static string Digest(long revision, string payload) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(revision.ToString(CultureInfo.InvariantCulture) + "\n" + payload)));

    private static void Replace(SqliteConnection connection, SqliteTransaction transaction, long revision, string payload, bool insert)
    {
        using var command = Command(connection, transaction, insert
            ? "INSERT INTO checkpoint VALUES(1, $revision, $payload, $digest)"
            : "UPDATE checkpoint SET revision=$revision, payload=$payload, digest=$digest WHERE id=1 AND revision=$previous");
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$previous", revision - 1);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$digest", Digest(revision, payload));
        if (command.ExecuteNonQuery() != 1) throw new CheckpointStorageException(StorageFailure.StaleRevision);
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 1;
        return command;
    }

    private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    private void CheckMarker()
    {
        if (!Present(PathFor(MarkerName))) throw new CheckpointStorageException(StorageFailure.MissingEstablishedState);
        if (File.ReadAllText(PathFor(MarkerName)) != MarkerContents) throw new CheckpointStorageException(StorageFailure.Corruption);
    }

    private void CheckRecovery()
    {
        if (Present(PathFor(RecoveryName))) throw new CheckpointStorageException(StorageFailure.InterruptedRecovery);
    }

    private void CheckWritable()
    {
        if ((File.GetAttributes(PathFor(DatabaseName)) & FileAttributes.ReadOnly) != 0)
            throw new CheckpointStorageException(StorageFailure.AccessFailure);
        // Reserve/write access to the directory is required for rollback journals.
        // DeleteOnClose makes this empty capability probe ephemeral.
        using var probe = new FileStream(PathFor("write-probe-" + Guid.NewGuid().ToString("N")),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        probe.Flush(flushToDisk: true);
    }

    private bool EnsureRecoveryCompatible(string sourceDirectory, bool recoverJournal = false)
    {
        var source = Path.Combine(sourceDirectory, DatabaseName);
        if (!Present(source)) return false;
        RejectFutureHeader(source);
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = source,
                Mode = recoverJournal ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
                DefaultTimeout = 1
            }.ToString());
            connection.Open();
            var version = Convert.ToInt64(Scalar(connection, null, "PRAGMA user_version"), CultureInfo.InvariantCulture);
            if (version is not 0 and not 1) throw new CheckpointStorageException(StorageFailure.UnsupportedVersion);
            if (version == 1 && Scalar(connection, null, "SELECT payload FROM checkpoint WHERE id=1") is string payload)
            {
                try { CheckpointCodec.Decode(payload, utcNow()); }
                catch (CheckpointStorageException e) when (e.Failure == StorageFailure.InvalidPayload) { }
            }
        }
        // A corrupt database is recoverable; an incompatible readable one is not.
        catch (SqliteException e) when (e.SqliteErrorCode is 1 or 11 or 26) { }
        catch (SqliteException e) when (e.SqliteExtendedErrorCode == 776 && !recoverJournal)
        {
            // SQLITE_READONLY_ROLLBACK: defer payload inspection until originals
            // have been archived and SQLite can recover a disposable copy.
            return true;
        }
        return false;
    }

    private static void RejectFutureHeader(string source)
    {
        // Reject a known future storage version even when damaged schema/journals
        // prevent SQLite from evaluating PRAGMA user_version.
        using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[100];
        if (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length &&
            header[..16].SequenceEqual("SQLite format 3\0"u8))
        {
            var version = BinaryPrimitives.ReadInt32BigEndian(header[60..64]);
            if (version is not 0 and not 1) throw new CheckpointStorageException(StorageFailure.UnsupportedVersion);
        }
    }

    private static bool Present(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void WriteFlushed(string path, string contents, FileMode mode)
    {
        using var file = new FileStream(path, mode, FileAccess.Write, FileShare.None);
        file.Write(Encoding.UTF8.GetBytes(contents));
        file.Flush(flushToDisk: true);
    }

    private static void CopyFlushed(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private string PathFor(string name) => Path.Combine(directory, name);
    private static bool IsStorageError(Exception e) => e is IOException or UnauthorizedAccessException or SqliteException;
    private static CheckpointStorageException Map(Exception e) => new(e is SqliteException sql ? sql.SqliteErrorCode switch
    {
        5 or 6 => StorageFailure.Busy,
        11 or 26 => StorageFailure.Corruption,
        _ => StorageFailure.AccessFailure
    } : StorageFailure.AccessFailure);

    private void ThrowDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            lease.Dispose();
        }
    }
}
