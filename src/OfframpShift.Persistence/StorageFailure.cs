namespace OfframpShift.Persistence;

public enum StorageFailure
{
    MissingEstablishedState, InvalidPayload, Corruption, UnsupportedVersion,
    AccessFailure, Busy, StaleRevision, UncertainCommit, InterruptedRecovery
}

// Never include SQL, paths, serialized contents, or underlying exception messages.
public sealed class CheckpointStorageException(StorageFailure failure) : Exception($"Checkpoint storage: {failure}.")
{
    public StorageFailure Failure { get; } = failure;
}

internal enum StorageFault { BeforeUpdate, AfterUpdate, AfterCommit, BeforePreservation, AfterPreservation, AfterReplacement }
