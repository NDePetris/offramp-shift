# Transactional local checkpoint persistence (PR 2)

PR 4 extends payload compatibility to strict version-1 reads and version-2 writes,
with atomic migration before Ready and exact pending migration retry. SQLite
schema/user_version remains 1, and transaction/integrity/delivery guarantees below
are retained. See [migration and current domain state](best-effort-monitoring.md).
Aggregate Save diagnostics do not change persistence ordering or buffer writes.

`OfframpShift.Persistence` is a platform-independent `net10.0` integration around
the unchanged Core contracts. It adds strict checkpoint JSON, a local SQLite
store and a durable monitoring session. It adds no host, live observation,
notifications, scheduler, history tables, migration framework or network service.
Automatic switching and production browser attribution remain **NO-GO**.

## Ownership and operations

Use `DurableMonitoringSession.Open(clock, initialConfiguration, directory)`.
An omitted directory resolves through `LocalApplicationData` to
`%LOCALAPPDATA%\Offramp\Shift` on Windows. No administrator privilege is needed.
Explicit directories support tests and future hosts. Do not share a directory
with an unrelated application or modify its files while the session is open.

The session privately owns its coordinator and store. `Status` and `Failure`
describe startup and persistence availability. `State` and `Configuration` are
available only when Ready; placeholder recovery state is never published as a
fresh allowance. Observe, configuration, pause and snooze serialize the whole
transition, single checkpoint capture and commit through one gate. Every Core
`CheckpointRequired` transition writes, including continuity changes and
invalidations. Duplicate/pure no-op transitions do not write.

Successful calls return Core `TransitionResult` values, including advisory
effects, only after commit. The session executes no callback or notification
sink. A future host delivers returned effects outside the session lock, orders
concurrent delivery by effect sequence, and suppresses obsolete effects. Delivery
failure never undoes a commit or causes replay. A crash between commit and
delivery can lose a reminder; this is the accepted tradeoff.

On save failure the call throws a typed `CheckpointStorageException`, returns no
effects, retains the exact checkpoint and enters PendingCommit. Every subsequent
observation/command and state publication is blocked. `RetryPendingCommit()` is
explicit: it does not replay the domain operation or recapture UTC metadata.
An interruption before commit leaves the previous revision; acknowledgment loss
can leave the next revision with the exact pending payload. These are the only
accepted outcomes. The latter is acknowledged without another replacement.
Conflicting revisions or same-revision payload changes block further recovery;
unsupported versions become Incompatible. Unusable storage requires explicit
reinitialization; busy/access failures retain the pending commit for another
explicit retry. There is no application retry loop.

Unexpected exceptions during transition/capture/save also fail closed. If capture
did not complete, the session is Blocked and must be disposed/reopened to restore
the last durable checkpoint; it never publishes mutated memory. If an exact
checkpoint was captured, it remains pending for explicit retry. Retry tracks Save
acknowledgment separately from restoration, so a later clock/restore exception
cannot cause a second replacement of an already acknowledged checkpoint. A fresh
Load rechecks revision/payload before restoration.

The session rejects same-thread reentry from host clock getters, including state
reads, nested commands and disposal. C# Monitor locks are reentrant, so the gate
alone cannot protect these paths. Other threads still serialize through the gate.

After retry success, restore the committed checkpoint through Core. Partial
break/idle continuity and old monotonic evidence are discarded; confirmed usage,
configuration, pause, suppression, timer remainders, completed-break latch and
effect sequence survive. All delayed effects are discarded. Restart and retry
never account for downtime or reuse the old monotonic baseline.

## Store format and transactions

`SqliteCheckpointStore` implements unchanged `ICheckpointStore`. `Load()` returns
null only after establishing the absence of all checkpoint artifacts and recovery
directories under the exclusive lease. Load must precede Save. There are no
long-lived database connections or pools; each operation opens a fresh private
connection. Dispose closes the lease.

The database is `checkpoint.db`. SQLite `application_id` is `0x4f535346` and
`user_version` is 1, independent of domain checkpoint version 1. A strict table
has one row (`id = 1`), a positive signed 64-bit revision, complete payload and
hex SHA-256 digest. The hash covers invariant decimal revision, a newline and
the UTF-8 payload. It detects accidental corruption only: it provides no
authentication or tamper resistance.

Initialization creates the schema and checkpoint at revision 1 in one immediate
transaction. Save validates identity, schema, integrity, digest, domain state and
expected revision in an immediate transaction; then replaces the entire payload,
digest and incremented revision together. Revision overflow fails closed.
Extra application schema objects, unexpected journal mode and malformed rows
are unusable. `PRAGMA integrity_check` is required before restoration and writes.

Rollback-journal DELETE mode and `synchronous=EXTRA` are selected and verified.
Existing storage is never silently converted from another journal mode.
Connection/command lock timeouts are one second, including implicit transaction
commands. The provider's bounded busy handling is used without application
retries. Directory write capability and database read-only attributes are checked
before startup becomes Ready. Later access changes still fail closed on Save.

The codec explicitly requires every version-1 field at every object level. It
rejects duplicate/unknown/missing fields, null required values, incorrect JSON
types, unsupported versions, invalid configuration, inconsistent domain state,
default/non-UTC save metadata. Future timestamps are still rejected for restoration
and writes, but otherwise domain-valid future metadata produces ClockUnavailable
rather than InvalidPayload. Configuration defaults cannot fill gaps.
Unknown versions are rejected before applying the version-1 layout. Public
diagnostics contain only failure categories, never payloads, SQL or underlying
exception text.

A backward host UTC change can make previously committed metadata appear future.
Startup then becomes Blocked, with destructive Reinitialize unavailable. Correct
the host clock and dispose/reopen; a pending Save may be explicitly retried once
the clock permits validation. No timestamp is rewritten, no checkpoint is accepted
while future, and Core's IsValid/Restore contract is unchanged. A genuinely invalid
domain checkpoint remains InvalidPayload even if its timestamp is also future.
There is no way to prove why a structurally valid timestamp is future from this
store alone; temporarily blocking either case is the conservative choice.

## Lease, initialization and recovery

`owner.lock` is a persistent file opened with exclusive sharing for the entire
store lifetime. It is never routinely deleted or archived. A competing process
receives Busy. This file is a lease only; its existence does not establish that
initialization occurred. It deliberately differs from the initialization marker
so Windows sharing locks cannot prevent archiving marker contents.

`initialized` has exact versioned contents and is exclusively created and flushed
before database creation. No artifacts means first launch; marker without a
database, missing row, orphaned database, malformed marker, interrupted schema
initialization, invalid checkpoint or corruption means RecoveryRequired. Filesystem
attribute operations distinguish absence from access failure. Busy, permission
and unavailable storage produce Blocked; supported-state restoration never falls
back to memory-only accounting or a fresh constructor.

The host must obtain explicit user authorization for `Reinitialize(configuration)`;
this abandons unavailable history. It is available only in RecoveryRequired,
never for incompatible versions or competing owners. Core's existing Reinitialize
operation produces replacement state and one checkpoint. The store retains its
lease, with all connections closed, and performs the following protocol:

1. Refuse detectable unsupported versions, including a version changed since
   startup. Choose a unique local `recovery-<guid>` directory name.
2. Preserve an earlier recovery intent, if present, then write and flush
   `recovery.pending` containing the new archive directory name. It is durable
   intent, not permission to resume automatically. For a new recovery, intent
   precedes even archive directory creation; an earlier intent protects that
   operation when an old intent must first be copied.
3. Copy and flush the original database, journal/WAL/SHM companions and marker
   bytes into the archive. Flush its `preserved` record only after all copies
   succeed. If copying fails, keep original database/companion/marker files
   untouched and block replacement. Incomplete archives are retained.
4. Only after preservation succeeds, remove original checkpoint files, create
   and flush the initialization marker, and transactionally initialize schema
   and replacement checkpoint. Remove recovery intent only after success.
5. Restore the new committed checkpoint before publishing Ready.

Recovery preflight uses a read-only connection so it does not roll back a journal
in originals before archiving. If SQLite reports READONLY_ROLLBACK, its fixed
header version is checked, then all originals are preserved before SQLite recovers
a separate disposable copy in the archive's `journal-validation` subdirectory.
That copy is checked for storage/domain compatibility before originals may be
replaced. A future domain version obscured by a journal cannot bypass rejection.
Original archive bytes remain unchanged; validation copies are retained locally
with the archive. Detectable unsupported versions cannot be reinitialized. No
archived checkpoint is automatically restored into active storage.

Any recovery intent on startup, even malformed/empty, requires recovery. Surviving
recovery directories with missing active artifacts also block first-launch
initialization. Interruption after preservation or after replacement commit but
before intent removal remains blocked. A further explicit Reinitialize preserves
the current artifacts and prior intent in a new archive. Archives are never
automatically deleted. Activation, tray UX and general single-instance UX are
future host work.

## Privacy, durability and limitations

Only the existing checkpoint and necessary storage metadata are persisted.
Configured opaque application keys are configuration. Observation identities,
titles, URLs, window/process identifiers, input contents, clock epochs and
activity history are excluded. Runtime has no network, synchronization, telemetry
or account dependency. Payloads and recovery copies are local plaintext; filesystem
permissions remain the host's responsibility.

SQLite durability and flushed marker/copy contents remain conditional on the
filesystem and hardware honoring flushes. Multi-file initialization/recovery is
guarded by intent rather than one filesystem transaction. Directory-entry
durability, reboot, actual power loss, disk-full and hardware faults need separate
live validation. Synchronized Windows process termination has been exercised at
the documented barriers; mid-system-call/native-COMMIT termination remains untested.
Injected exceptions execute normal disposal/rollback; they do not prove crash or
power-loss behavior. Local filesystems
that honor the lease and SQLite locking are assumed; remote/cloud-backed folders
are not validated.

Whole-store deletion or replacement with a complete older consistent copy cannot
be detected reliably using that same store alone. Revisions detect conflicts
within a session, not trusted historical rollback across restarts. No second
trusted ledger is added. Replacing/removing the lease behind an owner, malicious
editing and tamper-proof accounting are outside the guarantee.

Version 1 has no migrations or downgrade path. Future migrations must be explicit,
transactional, preserve originals and validate resulting domain checkpoints.

See [storage decision](../decisions/local-storage.md),
[validation evidence and live checklist](../validation/pr2-persistence.md) and
[dependency notices](../../THIRD-PARTY-NOTICES.md).
