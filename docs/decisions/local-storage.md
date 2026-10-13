# Local checkpoint storage decision

Status: approved for PR 2; production host and distribution remain undecided.

Use SQLite through pinned Microsoft.Data.Sqlite with bundled native SQLite,
without an ORM. Atomic file replacement would require custom flushing, locking,
initialization and interrupted-write handling. SQLite supplies conventional
transaction/crash-recovery machinery. Future summaries are secondary; this
change adds only a singleton checkpoint table, no activity/history tables.
[Microsoft documents the bundled dependency](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions).

Use rollback-journal DELETE mode and `synchronous=EXTRA`, verifying both settings.
This avoids WAL management and selects SQLite's strongest rollback-journal
durability setting. The filesystem and hardware must honor flushes.
[SQLite documents these settings](https://www.sqlite.org/pragma.html#pragma_synchronous).

Microsoft.Data.Sqlite is pinned to 10.0.12; its bundled SQLitePCLRaw graph resolves
to 2.1.12. Both persistence projects commit dependency lock files; the root runner
restores in locked mode. NuGet source mapping limits new dependencies to the
provider/bundle families (and System.Memory for applicable framework dependency
graphs). Vulnerability auditing and warnings-as-errors remain enabled. The
initial 10.0.0 candidate was rejected by auditing because native 2.1.11 is affected
by [CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).

An exclusive persistent lease file protects each store session; the initialization
marker is separate to permit Windows recovery archiving. A flushed recovery-intent
file makes interrupted explicit replacement detectable across restarts. Original
database, companion and marker bytes must be preserved before replacement. No
silent fallback, backup restoration, downgrade or second trusted ledger is added.

SHA-256 detects accidental corruption, without authenticating data. Whole-store
deletion or complete-copy rollback remains undetectable from that store alone.
Effects return only after commits and are delivered by the future host outside
the session lock; a reminder can be lost after commit without being replayed.

See [the architecture](../architecture/local-persistence.md) for failure states,
recovery ordering and durability limitations. Automatic desktop switching and
production browser attribution remain **NO-GO**. GPLv3 development licensing and
public distribution decisions remain unchanged.
