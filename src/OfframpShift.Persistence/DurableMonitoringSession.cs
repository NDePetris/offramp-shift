using OfframpShift.Core;

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
    private PersistenceStatus status;
    private CheckpointStorageException? failure;

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
                store.Save(coordinator.CreateCheckpoint());
            }
            else coordinator = MonitoringCoordinator.Restore(clock, fallback, checkpoint);
            status = PersistenceStatus.Ready;
        }
        catch (CheckpointStorageException e) { SetStartupFailure(e); }
    }

    public TransitionResult Observe(MonitoringObservation observation) => Apply(() => coordinator.Observe(observation));
    public TransitionResult ChangeConfiguration(MonitoringConfiguration configuration) => Apply(() => coordinator.ChangeConfiguration(configuration));
    public TransitionResult SetPaused(bool paused) => Apply(() => coordinator.SetPaused(paused));
    public TransitionResult Snooze() => Apply(() => coordinator.Snooze());

    private TransitionResult Apply(Func<TransitionResult> transition)
    {
        lock (gate)
        {
            RequireReady();
            var result = transition();
            if (result.State.Recovery == RecoveryStatus.Required)
            {
                SetStartupFailure(new(StorageFailure.InvalidPayload));
                throw failure!;
            }
            if (!result.CheckpointRequired) return result;
            pending = coordinator.CreateCheckpoint();
            try
            {
                store!.Save(pending);
                pending = null;
                return result;
            }
            catch (CheckpointStorageException e)
            {
                failure = e;
                status = PersistenceStatus.PendingCommit;
                throw;
            }
        }
    }

    // No transition or timestamp is regenerated and no delayed effects are returned.
    public void RetryPendingCommit()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(status == PersistenceStatus.Disposed, this);
            if (status != PersistenceStatus.PendingCommit || pending is null) throw new InvalidOperationException("No pending commit.");
            try
            {
                store!.Save(pending);
                coordinator = MonitoringCoordinator.Restore(clock, fallback, pending);
                pending = null;
                failure = null;
                status = PersistenceStatus.Ready;
            }
            catch (CheckpointStorageException e)
            {
                failure = e;
                if (e.Failure is StorageFailure.StaleRevision or StorageFailure.UnsupportedVersion)
                    status = e.Failure == StorageFailure.UnsupportedVersion ? PersistenceStatus.Incompatible : PersistenceStatus.Blocked;
                else if (e.Failure is StorageFailure.Corruption or StorageFailure.InvalidPayload or StorageFailure.MissingEstablishedState or StorageFailure.InterruptedRecovery)
                    status = PersistenceStatus.RecoveryRequired;
                throw;
            }
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
            if (status != PersistenceStatus.RecoveryRequired || store is null) throw new InvalidOperationException("Explicit recovery is unavailable.");
            coordinator = MonitoringCoordinator.Restore(clock, fallback, null);
            coordinator.Reinitialize(replacement);
            pending = coordinator.CreateCheckpoint();
            try
            {
                store.Reinitialize(pending);
                coordinator = MonitoringCoordinator.Restore(clock, fallback, pending);
                pending = null;
                failure = null;
                status = PersistenceStatus.Ready;
            }
            catch (CheckpointStorageException e)
            {
                // Recovery is never resumed implicitly or via an ordinary save retry.
                SetStartupFailure(e.Failure == StorageFailure.UnsupportedVersion ? e : new(StorageFailure.InterruptedRecovery));
                throw;
            }
        }
    }

    private void SetStartupFailure(CheckpointStorageException e)
    {
        failure = e;
        status = e.Failure switch
        {
            StorageFailure.UnsupportedVersion => PersistenceStatus.Incompatible,
            StorageFailure.AccessFailure or StorageFailure.Busy or StorageFailure.StaleRevision => PersistenceStatus.Blocked,
            _ => PersistenceStatus.RecoveryRequired
        };
    }

    private void RequireReady()
    {
        ObjectDisposedException.ThrowIf(status == PersistenceStatus.Disposed, this);
        if (status != PersistenceStatus.Ready) throw failure ?? new CheckpointStorageException(StorageFailure.MissingEstablishedState);
    }

    public void Dispose()
    {
        lock (gate)
        {
            store?.Dispose();
            status = PersistenceStatus.Disposed;
        }
    }
}
