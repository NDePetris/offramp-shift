using Microsoft.Data.Sqlite;
using OfframpShift.Core;
using OfframpShift.Persistence;

internal sealed class Clock : IMonitoringClock
{
    public MonotonicTime Now { get; set; } = new(Guid.NewGuid(), 0);
    private DateTimeOffset utcNow = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    public Func<DateTimeOffset, DateTimeOffset>? UtcRead { get; set; }
    public DateTimeOffset UtcNow { get => UtcRead?.Invoke(utcNow) ?? utcNow; set => utcNow = value; }
    public MonitoringObservation Interval(long duration, DesktopScope desktop = DesktopScope.Personal,
        InputState input = InputState.Active, EvidenceConfidence confidence = EvidenceConfidence.Confirmed)
    {
        var start = Now;
        Now = Now with { Milliseconds = Now.Milliseconds + duration };
        return new(start, Now, desktop, SessionState.Active, new("opaque-app", true), input,
            MonitoringAvailability.Available, confidence);
    }
}

internal sealed class Fixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "offramp-persistence-" + Guid.NewGuid().ToString("N"));
    public Clock Clock { get; } = new();
    public static MonitoringConfiguration Small => new()
    {
        AllowanceMs = 30,
        RequiredBreakMs = 15,
        IdleThresholdMs = 6,
        GraceMs = 6,
        RepeatNudgeMs = 10,
        SnoozeMs = 10,
        MaximumEvidenceAgeMs = 5
    };
    public Fixture() => Directory.CreateDirectory(DirectoryPath);
    public string FilePath(string name) => Path.Combine(DirectoryPath, name);
    public DurableMonitoringSession Session(Action<StorageFault>? fault = null) => new(Clock, Small, DirectoryPath, fault);
    public SqliteCheckpointStore Store() => new(DirectoryPath, () => Clock.UtcNow);
    public MonitoringCheckpoint Checkpoint => new MonitoringCoordinator(Clock, Small).CreateCheckpoint();
    public void Initialize()
    {
        using var session = Session();
        Program.Equal(PersistenceStatus.Ready, session.Status);
    }
    public SqliteConnection Connection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = FilePath(SqliteCheckpointStore.DatabaseName),
            Pooling = false,
            DefaultTimeout = 1
        }.ToString());
        connection.Open();
        return connection;
    }
    public void Sql(string sql)
    {
        using var connection = Connection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    public (long Revision, string Payload) Row()
    {
        using var connection = Connection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision, payload FROM checkpoint";
        using var rows = command.ExecuteReader();
        rows.Read();
        return (rows.GetInt64(0), rows.GetString(1));
    }
    public void Payload(string payload, long? revision = null)
    {
        var next = revision ?? Row().Revision;
        using var connection = Connection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE checkpoint SET revision=$revision, payload=$payload, digest=$digest";
        command.Parameters.AddWithValue("$revision", next);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$digest", SqliteCheckpointStore.Digest(next, payload));
        command.ExecuteNonQuery();
    }
    public void Dispose()
    {
        // Unique owned test directory; all connections/children must already be closed.
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(DirectoryPath, recursive: true);
    }
}
