# PR 2 final review and Windows termination validation — October 13, 2026

Prepared change title: **feat: add transactional local checkpoint persistence**.
Base: remote/main `3a1bba9`. Local feature branch:
`feat/transactional-local-persistence`. Final review authorizes pushing and opening
a PR after final checks; merging, deployment, publishing and release remain excluded.

## Final review and corrected revision

Reviewed baseline: `3a24e24`. Corrected code, regression tests, termination harness
and architecture: **`5d2515da79560d5b6e0e55f810b30ca039542fd2`**. This follow-up
report changes documentation only; final checks are repeated at the report's
containing commit, with its exact SHA recorded in the PR description. Base main
remains `3a1bba9`. The original validation below is retained as historical evidence.

Confirmed findings, ordered by severity, are corrected in that code revision:

1. **P1 — failure containment and reentry.** In the original
   `DurableMonitoringSession.Apply`, checkpoint capture was outside the exception
   boundary and save handling caught only typed storage exceptions. A throwing
   clock getter after mutation or an unexpected transaction exception could leave
   Ready and expose uncommitted usage. Monitor locks also permit same-thread
   reentry from a clock getter, allowing state reads/nested commands/disposal
   during commit. The whole lifecycle now fails closed and rejects reentry.
   See [Apply](../../src/OfframpShift.Persistence/DurableMonitoringSession.cs#L68)
   and [reentry guard](../../src/OfframpShift.Persistence/DurableMonitoringSession.cs#L206).
2. **P1 — recovery could overwrite an incompatible domain version.** A journal
   can make the read-only preflight report READONLY_ROLLBACK. The original guard
   checked only storage header version 1, allowing a future domain payload behind
   that journal to be abandoned through recovery. Originals are now preserved
   first, and a separate recoverable validation copy must pass version inspection
   before replacement. Known future storage headers are rejected even when schema
   damage prevents SQLite inspection. See
   [recovery validation](../../src/OfframpShift.Persistence/SqliteCheckpointStore.cs#L161).
3. **P2 — backward UTC was misclassified as corrupt history.** Previously valid
   SavedUtc could appear future after clock rollback and become RecoveryRequired,
   offering destructive reinitialization. Structurally/domain-valid future
   metadata now becomes ClockUnavailable/Blocked at startup, with no destructive
   recovery. Correct UTC and reopen, or explicitly retry a pending commit. Future
   checkpoints still fail IsValid(now); invalid domain state remains invalid.
   Core's contract is unchanged. A second clock read during Restore is checked
   so a recovery placeholder cannot be published with Ready. See
   [codec validation](../../src/OfframpShift.Persistence/CheckpointCodec.cs#L65)
   and [checked restoration](../../src/OfframpShift.Persistence/DurableMonitoringSession.cs#L186).
4. **P2 — acknowledgment and restoration needed separate tracking.** A restore
   failure after successful pending Save could prompt another full replacement
   on retry. The session now retains Save acknowledgment, reloads/rechecks the
   committed row and retries restoration without another revision increment.
   See [pending retry](../../src/OfframpShift.Persistence/DurableMonitoringSession.cs#L102).

Additional hardening moves new recovery intent ahead of archive directory
creation. Existing intent still protects preservation of earlier intent. New
assertions cover unexpected capture/transaction/recovery exceptions, clock
rollback and restore races, reentry, acknowledged retries, hidden future domain
versions and damaged future storage schema. No unresolved blocking defect was
found in the approved scope. Hardware/filesystem claims below remain risks,
not proven guarantees.

## Current automated and actual Windows evidence

Host: Windows **25H2, build 26200.9457**, OS version **10.0.26200**, **x64**.
Disposable directories use the current user's Windows temporary directory on
**NTFS**. SDK: repository-local **.NET 10.0.401**. These are automated tests on an
actual Windows host, not manual UI acceptance. No system clock was changed:
clock adjustments were simulated using the injected clock. Ordinary host
processes and shell were not terminated.

| Required check | Result |
|---|---|
| `./run.ps1 test` — Core | 58 tests / 38,654 assertions PASS |
| `./run.ps1 test` — Persistence | 56 tests / 544 parent-process assertions PASS, including 5 actual termination scenarios |
| `./run.ps1 format` | PASS for both libraries and assertion projects |
| Release builds / locked restore | PASS, zero warnings/errors; unchanged dependency locks and auditing |
| `./spikes/VirtualDesktop/run.ps1 test` | 58 policy tests and 3 worker isolation checks PASS |
| `./spikes/BrowserAttribution/run.ps1 test` | 2,053 policy/protocol assertions, 5 mocked extension groups, manifest/syntax and synthetic native transport PASS |
| Complete scope/diff and whitespace checks | PASS; Core source, spike behavior and dependency graph unchanged |

Exception-injection tests still exercise managed unwinding and Dispose. They
remain simulations, including the future-domain/companion rejection regression.
The five tests below instead call Windows forced process termination on the exact
dedicated child Process object, while it is stopped at a stdout synchronization
barrier. Stdin is not closed before killing it, no graceful shutdown is requested,
and no writer Dispose/finally runs. A separately launched verifier then acquires
the released lease and checks storage. Only owned GUID-named disposable temp
directories are accepted by the test-only commands; reparse-point targets are
rejected. The root test runner includes these checks on Windows and reports them
not run on other platforms.

| Actual termination boundary | Observed result |
|---|---|
| After acknowledged durable Save with usage 33 ms, Snoozed, 7 ms remaining, configured opaque exclusion and sequence 1 | Fresh process restored the exact complete payload. No reset/downtime accounting/replay; the next certified 1 ms interval yielded usage 34 ms and zero effects. |
| After replacement SQL update, before COMMIT, in a genuinely open store transaction | An approximately 8 MiB configuration payload forced native cache spill. Parent confirmed the nonzero 8-byte hot rollback-journal magic and expanded DB file before killing writer. Fresh process exercised SQLite recovery and restored the exact previous complete payload (usage 5 ms); next certified interval yielded 6 ms, zero effects. |
| After recovery intent flushed, before copying originals | Restart explicitly reported InterruptedRecovery. Original database/marker bytes remained intact; further explicit recovery succeeded and both archive directories remained. |
| After all originals copied/flushed and preservation completed, before replacement | Archive database/marker bytes matched originals, with preserved record and intent. Restart stayed blocked; further explicit recovery succeeded without deleting archives. |
| After replacement checkpoint committed, before clearing recovery intent | Despite a valid replacement checkpoint, restart stayed blocked. Original archive bytes remained intact; further explicit recovery succeeded and archives were retained. |

Each paused writer excluded a competing cooperating store with Busy. Its forced
termination released ownership for the newly launched verifier. Barriers are
inside the existing internal test hooks; no production process orchestration,
notification delivery, scheduler, or locking framework is added. Full payload
comparison occurs before a new observation changes state. Test directories are
removed only after all owned children exit.

This establishes behavior at those synchronized application boundaries on the
observed Windows/NTFS host. It does **not** establish termination midway through
native COMMIT, a filesystem syscall/copy/delete/flush, reboot, power loss,
disk-controller/cache behavior, true disk-full/denied-directory ACL recovery,
remote/cloud-backed filesystems or other platforms. System/hardware failure can
violate assumptions about flush and directory-entry durability. There is no claim
that process termination proves power-loss durability or production monitoring.

Architecture changes remain within PR 2: broader fail-closed handling, reentry
rejection, non-destructive clock unavailability, acknowledgment tracking and
compatibility inspection of a recovered copy. No Core validity relaxation,
migration framework, history, host UI, live observation or network service was
introduced. Validation copies increase local recovery-archive space use; archives
are never silently deleted. Whole-store deletion/consistent older-copy rollback
remain undetectable locally; SHA-256 is accidental-corruption detection only.

Automatic desktop switching and production website attribution remain **NO-GO**.
Licensing/distribution remain undecided, GPLv3 development licensing unchanged.

## Historical initial validation at 3a24e24

### Initial automated pass

Executed on this Windows x64 host, build **26200.9457**, DisplayVersion 25H2,
with repository-local .NET SDK **10.0.401** and NTFS temporary storage. These
are automated local checks, not manual durability or Windows compatibility
acceptance. Monitor/hardware configuration is irrelevant to these tests and was
not used as evidence. Release builds preserve warnings-as-errors and SDK analyzers.

| Command / check | Result | Evidence boundary |
|---|---|---|
| `./run.ps1 test` — Core | 58 tests / 38,654 assertions PASS | Synthetic observations and injected clock; Core unchanged |
| `./run.ps1 test` — Persistence | 38 tests / 415 assertions PASS | Real temporary SQLite files, fresh connections, real competing child process; interruption injection is simulated |
| `./run.ps1 format` | PASS | SDK verify-no-changes for Core/persistence libraries and both assertion projects |
| Locked dependency restore / Release builds | PASS, zero warnings/errors | Pinned Microsoft.Data.Sqlite 10.0.12 and SQLitePCLRaw 2.1.12; auditing remains enabled |
| `./spikes/VirtualDesktop/run.ps1 test` | 58 policy tests and 3 worker isolation checks PASS | Fakes and disposable worker faults; no COM desktop switching exercised |
| `./spikes/BrowserAttribution/run.ps1 test` | 2,053 policy/protocol assertions, 5 mocked extension groups, syntax/manifest and synthetic transport PASS | Real local process/stdio/pipe communication with synthetic caller; no Chrome/Edge attribution |

Persistence coverage includes first launch and repeated full replacement;
restart accumulation, configuration, pause, snooze/suppression, completed resets
and completed-break latches; every strict JSON field, malformed JSON, invalid
domain state, future metadata and unsupported versions; missing/orphaned/truncated
or corrupted initialized storage, digest/identity/schema failures; read-only and
unavailable storage, locked marker access, same/competing-process leases, bounded
SQLite busy timeout, stale and same-revision conflicts; concurrent serialized
commands; Windows marker/companion preservation under the retained lease; failed
preservation; future-version recovery guards and surviving recovery archives.

### Initial simulated interruption evidence

Internal test hooks inject IOException before update, after update before commit,
and after commit before acknowledgment, inside real SQLite transactions. Tests
compare complete previous/new checkpoint payloads, resolve exact retained retries
at one revision increment without new UTC metadata, block subsequent commands
and state publication, and establish a fresh observation baseline after retry.
Restart after a committed exhaustion never replays its effect or charges downtime.
Initialization interruption never admits an incomplete installation as first run.

Recovery faults are injected before preservation, after all originals are
preserved and after replacement commit. Every flushed intent remains detectable
on restart, including when the replacement checkpoint is valid. A further
explicit recovery archives current artifacts and the earlier intent. A real
Windows sharing lock on the source marker causes preservation failure: original
database and marker bytes remain intact, and replacement stays blocked.

These tests run managed exception unwinding and connection disposal. They do not
terminate a writer during native commit or emulate missing hardware flushes.
The synthetic companion-file test exercises preservation, not native hot-journal
recovery after real process death.

### Initial manual evidence, untested guarantees and gates

**Manual durability acceptance: not performed.** No process kill during an active
transaction, Windows reboot, sudden power loss, physical storage fault, actual
disk-full exercise or network/cloud-backed filesystem test was performed. Normal
automated SQLite transactions and file Flush(true) calls cannot certify those
conditions. General host activation/tray UX and notification delivery do not
exist. No Linux/macOS runtime validation was performed despite platform-independent
project targets.

Before claiming supported durability, record exact OS/build, commit, filesystem,
drive/controller/cache configuration, privileges and evidence for each deliberate
live exercise on disposable data:

- Kill a separate writer at initialization/update/commit/recovery boundaries;
  restart and verify complete previous/new checkpoints or explicit blocked recovery.
- Exercise real hot-journal recovery after process termination; verify identity,
  schema, integrity and domain validity and no duplicated interventions.
- Interrupt recovery during copying, deleting originals, committing replacement
  and clearing intent; verify archives survive and ambiguous runs remain blocked.
- Reboot and, only in a controlled disposable hardware setup, test actual loss of
  power. Examine marker, intent, archive and directory-entry durability separately
  from SQLite commit durability.
- Exercise real disk full, denied directory ACLs and interrupted device access;
  preserve originals and confirm no monitoring/effects escape on write failure.
- Verify any additional filesystem/platform separately; do not generalize NTFS
  sharing/lock results to remote folders, synchronization tools or other OSes.

**Blocked/NO-GO:** automatic virtual-desktop switching and production browser
website attribution remain NO-GO. Existing spike jobs and behavior are unchanged.
Licensing/distribution remain undecided; GPLv3 development licensing is unchanged.

### Initial architecture refinements and residual risks

No material departure from the approved SQLite design. Effects return after
successful commits, with host delivery outside the session gate. The exclusive
lease uses a separate persistent `owner.lock` file; `initialized` remains readable
for recovery archiving. A flushed `recovery.pending` file and unique local recovery
directories detect interrupted replacement without automatically resuming it.
Unsupported versions are checked again before explicit recovery. Startup checks
write capability before Ready, while later access changes still fail closed.

SQLite's DELETE/EXTRA and flushed copies depend on the filesystem/hardware honoring
flushes. Recovery spans multiple files and can require another explicitly authorized
reinitialization after interruption; preserved archives are not automatically
deleted/restored. Archives consume storage and contain the same local plaintext
configuration/checkpoint data. A reminder can be lost between commit and delivery.
Pending state not committed before process death can be lost; accounting does not
invent the missing interval. Complete-store deletion/older-copy replacement is
undetectable using that same store alone. SHA-256 detects accidental corruption
only and is not authentication or tamper-proofing.
