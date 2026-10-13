# PR 2 validation — October 13, 2026

Prepared change title: **feat: add transactional local checkpoint persistence**.
Base: remote/main `3a1bba9`. Local feature branch:
`feat/transactional-local-persistence`. No push, PR creation, merge, deployment
or release is part of this authorization.

## Automated pass

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

## Simulated interruption evidence

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

## Manual evidence, untested guarantees and gates

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

## Architecture refinements and residual risks

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
