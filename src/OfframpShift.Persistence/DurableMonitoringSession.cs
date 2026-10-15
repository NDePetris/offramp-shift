using OfframpShift.Core;
using System.Diagnostics;

namespace OfframpShift.Persistence;

public enum PersistenceStatus { Ready, PendingCommit, RecoveryRequired, Incompatible, Blocked, Disposed }

// Only immutable results escape; coordinator and store remain private. Returning
// effects after commit leaves delivery outside the gate and avoids callbacks.
public sealed class DurableMonitoringSession : IDisposable
{
    private readonly object gate = new();
    private readonly IMonitoringClock clock;
    private readonly MonitoringConfiguration fallback;
    private readonly SqliteCheckpointStore? store;
    private MonitoringCoordinator coordinator;
    private MonitoringCheckpoint? pending;
    private bool pendingSaved;
    private bool operationInProgress;
    private PersistenceStatus status;
    private CheckpointStorageException? failure;
    private readonly LatencyHistogram commitLatency = new();
    private long commitAttempts, commitAcknowledgments, commitFailures, noOpTransitions;

    public object CommitDiagnostics
    {
        get
        {
            lock (gate) return new
            {
                commitAttempts,
                commitAcknowledgments,
                commitFailures,
                noOpTransitions,
                preparedPayloadBytes = store?.PreparedPayloadBytes ?? 0,
                latency = commitLatency.Snapshot()
            };
        }
    }
    private void SaveCheckpoint(MonitoringCheckpoint checkpoint)
    {
        var started = Stopwatch.GetTimestamp(); commitAttempts++;
        try { store!.Save(checkpoint); commitAcknowledgments++; }
        catch { commitFailures++; throw; }
        finally { commitLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10); }
    }

    public PersistenceStatus Status { get { lock (gate) return status; } }
    public StorageFailure? Failure { get { lock (gate) return failure?.Failure; } }
    public EngineState State { get { lock (gate) { RequireReady(); return coordinator.State; } } }
    public MonitoringConfiguration Configuration { get { lock (gate) { RequireReady(); return coordinator.Configuration; } } }

    public static DurableMonitoringSession Open(IMonitoringClock clock, MonitoringConfiguration initialConfiguration, string? directory = null) =>
        new(clock, initialConfiguration, directory ?? SqliteCheckpointStore.DefaultDirectory, null);

    internal DurableMonitoringSession(IMonitoringClock clock, MonitoringConfiguration initialConfiguration,
        string directory, Action<StorageFault>? fault)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(initialConfiguration);
        initialConfiguration.Validate();
        this.clock = clock;
        fallback = initialConfiguration;
        coordinator = MonitoringCoordinator.Restore(clock, fallback, null);
        try
        {
            store = new(directory, () => clock.UtcNow) { Fault = fault };
            var checkpoint = store.Load();
            if (checkpoint is null)
            {
                coordinator = new(clock, fallback);
                SaveCheckpoint(coordinator.CreateCheckpoint());
            }
            else if (checkpoint.Version == 1)
            {
                // Validate the original before producing a version-2 replacement.
                // The existing store revision and exact pending payload protect migration.
                coordinator = MonitoringCoordinator.Restore(clock, fallback, checkpoint);
                if (coordinator.State.Recovery != RecoveryStatus.Ready)
                    throw new CheckpointStorageException(StorageFailure.ClockUnavailable);
                pending = coordinator.CreateCheckpoint();
                SaveCheckpoint(pending);
                pendingSaved = true;
                RestoreCommitted(pending);
                pending = null;
                pendingSaved = false;
            }
            else RestoreCommitted(checkpoint);
            status = PersistenceStatus.Ready;
        }
        catch (Exception e)
        {
            if (pending is not null)
            { failure = StorageException(e); status = PersistenceStatus.PendingCommit; }
            else SetStartupFailure(StorageException(e));
        }
    }

    public TransitionResult Observe(MonitoringObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return Apply(() => coordinator.Observe(observation));
    }
    public TransitionResult ChangeConfiguration(MonitoringConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        return Apply(() => coordinator.ChangeConfiguration(configuration));
    }
    public TransitionResult SetPaused(bool paused) => Apply(() => coordinator.SetPaused(paused));
    public TransitionResult Snooze() => Apply(() => coordinator.Snooze());

    private TransitionResult Apply(Func<TransitionResult> transition)
    {
        lock (gate)
        {
            RequireReady();
            operationInProgress = true;
            try
            {
                var result = transition();
                if (result.State.Recovery == RecoveryStatus.Required)
                {
                    SetStartupFailure(new(StorageFailure.InvalidPayload));
                    throw failure!;
                }
                if (!result.CheckpointRequired) { noOpTransitions++; return result; }
                pending = coordinator.CreateCheckpoint();
                SaveCheckpoint(pending);
                pending = null;
                return result;
            }
            catch (Exception e)
            {
                // Capture itself can fail after domain mutation. Never leave Ready
                // even when there is no exact checkpoint available to retry.
                if (pending is not null) status = PersistenceStatus.PendingCommit;
                else if (status != PersistenceStatus.RecoveryRequired) status = PersistenceStatus.Blocked;
                failure = StorageException(e);
                throw failure;
            }
            finally { operationInProgress = false; }
        }
    }

    // No transition or timestamp is regenerated and no delayed effects are returned.
    public void RetryPendingCommit()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(status == PersistenceStatus.Disposed, this);
            RequireNoOperation();
            if (status != PersistenceStatus.PendingCommit || pending is null) throw new InvalidOperationException("No pending commit.");
            operationInProgress = true;
            try
            {
                if (!pendingSaved)
                {
                    SaveCheckpoint(pending);
                    pendingSaved = true;
                }
                // A restore/clock failure after Save acknowledgment must retry only
                // restoration, while still checking for a conflicting committed row.
                var committed = store!.Load() ?? throw new CheckpointStorageException(StorageFailure.MissingEstablishedState);
                RestoreCommitted(committed);
                pending = null;
                pendingSaved = false;
                failure = null;
                status = PersistenceStatus.Ready;
            }
            catch (Exception error)
            {
                var e = StorageException(error);
                failure = e;
                if (e.Failure is StorageFailure.StaleRevision or StorageFailure.UnsupportedVersion)
                    status = e.Failure == StorageFailure.UnsupportedVersion ? PersistenceStatus.Incompatible : PersistenceStatus.Blocked;
                else if (e.Failure is StorageFailure.Corruption or StorageFailure.InvalidPayload or StorageFailure.MissingEstablishedState or StorageFailure.InterruptedRecovery)
                    status = PersistenceStatus.RecoveryRequired;
                throw e;
            }
            finally { operationInProgress = false; }
        }
    }

    // Host must obtain explicit user authorization: this abandons unavailable history.
    public void Reinitialize(MonitoringConfiguration replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        replacement.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(status == PersistenceStatus.Disposed, this);
            RequireNoOperation();
            if (status != PersistenceStatus.RecoveryRequired || store is null) throw new InvalidOperationException("Explicit recovery is unavailable.");
            operationInProgress = true;
            try
            {
                coordinator = MonitoringCoordinator.Restore(clock, fallback, null);
                coordinator.Reinitialize(replacement);
                pending = coordinator.CreateCheckpoint();
                pendingSaved = false;
                store.Reinitialize(pending);
                RestoreCommitted(pending);
                pending = null;
                failure = null;
                status = PersistenceStatus.Ready;
            }
            catch (Exception error)
            {
                var e = StorageException(error);
                // Recovery is never resumed implicitly or via an ordinary save retry.
                SetStartupFailure(e.Failure == StorageFailure.UnsupportedVersion ? e : new(StorageFailure.InterruptedRecovery));
                throw e;
            }
            finally { operationInProgress = false; }
        }
    }

    private void SetStartupFailure(CheckpointStorageException e)
    {
        failure = e;
        status = e.Failure switch
        {
            StorageFailure.UnsupportedVersion => PersistenceStatus.Incompatible,
            StorageFailure.AccessFailure or StorageFailure.Busy or StorageFailure.StaleRevision or
                StorageFailure.ClockUnavailable or StorageFailure.UnexpectedFailure => PersistenceStatus.Blocked,
            _ => PersistenceStatus.RecoveryRequired
        };
    }

    private void RestoreCommitted(MonitoringCheckpoint checkpoint)
    {
        // Core reads the clock again: a clock change between decode/save and
        // Restore must not publish its recovery placeholder with Ready status.
        CheckpointCodec.Validate(checkpoint, clock.UtcNow);
        coordinator = MonitoringCoordinator.Restore(clock, fallback, checkpoint);
        if (coordinator.State.Recovery != RecoveryStatus.Ready)
            throw new CheckpointStorageException(StorageFailure.ClockUnavailable);
    }

    private static CheckpointStorageException StorageException(Exception e) =>
        e as CheckpointStorageException ?? new(StorageFailure.UnexpectedFailure);

    private void RequireReady()
    {
        ObjectDisposedException.ThrowIf(status == PersistenceStatus.Disposed, this);
        RequireNoOperation();
        if (status != PersistenceStatus.Ready) throw failure ?? new CheckpointStorageException(StorageFailure.MissingEstablishedState);
    }

    private void RequireNoOperation()
    {
        // Monitor locks are reentrant. A host clock getter must not read mutated
        // state or recursively issue commands while an outer commit is pending.
        if (operationInProgress) throw new CheckpointStorageException(StorageFailure.Busy);
    }

    public void Dispose()
    {
        lock (gate)
        {
            RequireNoOperation();
            store?.Dispose();
            status = PersistenceStatus.Disposed;
        }
    }
}
