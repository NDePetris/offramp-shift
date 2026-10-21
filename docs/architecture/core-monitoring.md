# Core monitoring engine (PR 1)

PR 4 adds explicit BestEffort opt-in, Estimated confidence, QualifiedIdle and
bounded transient break preservation. CertifiedOnly retains the PR 1 behavior
described below. Checkpoint payloads now write version 2 and strictly migrate
version 1 through the durable session. See [the current best-effort contract](best-effort-monitoring.md)
for the added fields and where uncertainty/idle semantics differ; the original
certification limitations remain true for Confirmed evidence.

The domain library in `src/OfframpShift.Core` implements application classification,
usage, continuous breaks, advisory reminders, and recovery contracts. It has no
Windows, UI, browser, networking, or database dependency. The two feasibility
spikes remain isolated. Automatic desktop switching and production website
attribution remain **NO-GO**; see their existing validation documents.

## State ownership and integration

`MonitoringCoordinator` serializes all entry points through one lock and returns
immutable `EngineState` and `TransitionResult` values. It does not run callbacks,
poll, schedule timers, deliver notifications, or perform storage I/O. A future
adapter supplies `IMonitoringClock` and immutable `MonitoringObservation` values.
An integration must serialize the whole observe/command and checkpoint commit,
not only individual coordinator calls. PR 2's durable session returns effects
after commit; the future host orders delivery outside the session lock. Do not run a second
observation between capturing a transition and saving its matching checkpoint.

`ApplicationClassifier` uses an adapter-verified, stable opaque application key
with exact ordinal comparison. The adapter owns normalization and identity
verification. Keys must not be titles, process IDs, window handles, or browsing
data. Count-by-default excludes listed applications; blocklist-only includes
listed applications. Missing/unverified identity on active Personal is uncertain.
Chrome and Edge are ordinary applications. No website classification exists.

Configuration is immutable and validated before mutation. Rules apply only to
future intervals. Configuration commands discard partial break/idle continuity,
retain confirmed usage, and clamp existing timer remainders to the new durations.
Lowering an allowance below usage exhausts it immediately; increasing it does not
clear an existing exhaustion latch. A qualifying break is required to restore it.

## Interval evidence and time

An observation certifies the entire half-open `[Start, End)` interval as having
the supplied desktop, session, application, input, availability, and confidence.
**Certification is an explicit adapter responsibility.** An OS event, elapsed
timer tick, or equal foreground snapshots alone is not proof of uninterrupted
activity. An adapter must split at known transitions and submit uncertainty when
continuity, identity, session, or coverage cannot be established. This PR provides
no live adapter and does not demonstrate that Windows can supply this evidence.

Monotonic times have a process-local clock epoch and nonnegative milliseconds.
The injected clock establishes the initial boundary. Accepted intervals must
have positive duration, share the current epoch, start at or after the accepted
boundary, end no later than receipt time, and arrive within the configurable
receipt freshness limit (default five seconds). Freshness limits delivery lag,
not certified duration; simulations may certify long intervals explicitly.
No interval predating construction/restoration is counted.

An exact duplicate of the last accepted observation is a no-op. Other overlaps,
out-of-order samples, invalid times, and stale samples are rejected and clear
continuity. The rejection advances the baseline to receipt time without moving
it backward or into the future. A clock epoch change requires a new baseline;
same-epoch rollback cannot replay already charged time. Unexplained gaps earn
nothing and terminate continuity; a separately certified interval after a gap
can still count. Unknown/conflicting evidence clears continuity and advances no
usage or reminder/snooze timer. Already accepted usage is never retrospectively
reclassified by a later conflicting report; the adapter must resolve conflicts
before certifying intervals. There is no automatic retry or speculative repair.

Commands establish a boundary at their monotonic receipt time without charging
unsupplied time. A command with a regressing clock is rejected before mutation.
Repeated pause or snooze commands are idempotent. All accounting uses integer
milliseconds. Duration arithmetic is checked by range validation, counters cap
at their configured thresholds, and usage saturates at `long.MaxValue` rather
than wrapping. Exhausted usage continues accumulating until reset.

## Usage and continuous breaks

Only confirmed, unlocked, classified counted Personal intervals consume usage,
regardless of input inactivity (PR 11). Work, Other/excluded activity, lock, uncertainty, suspension,
and explicit pause consume none. Usage survives application changes, short
breaks, restart, and calendar rollover. Allowance exhaustion remains latched.

Breaks are independent from usage:

- Confirmed Work and confirmed lock earn credit immediately. Lock needs known
  desktop scope and monitoring confidence, but not foreground application/input.
- Input inactivity alone never earns break credit on Personal or Other. Active,
  Idle and QualifiedIdle use the existing Personal application inclusion/exclusion
  rules. Unknown input still fails closed for unlocked observations.
- Counted Personal and neutral/excluded activity end the break, including idle
  Personal after a partial Work break. Work and lock can connect when verified
  coverage is uninterrupted, regardless of input activity.
- Unknown desktop/session/input where required, conflicting/unavailable evidence,
  suspend, rejected intervals, gaps, and explicit pause clear partial continuity.
  Pause freezes usage and all timers, earning no reset credit. Resume starts fresh.
- Completion resets usage and interventions exactly once per uninterrupted
  qualifying break. A completed-break latch prevents repeated resets. Leaving
  that break rearms earning. Snooze at an already certified boundary does not
  interrupt break tracking.

Only supplied verified intervals earn credit; sleep, lock, or application downtime
is never inferred from a wall-clock gap. Precise lock/unlock and suspend/resume
segmentation remains the future Windows adapter's responsibility.

## Advisory interventions

PR 9 applies one app timing policy to both reminder styles: zero automatic grace
and shared `InteractiveRepeatNudgeMs`, default one minute, restricted to 1–60 whole
minutes. The field retains its original checkpoint-v3 name, avoiding a shape change.
The host always supplies the immediate policy, even while delivery is Off; there
is no Settings-time replay. A restored Grace remainder becomes zero on the next
counted Personal interval; a Nudging remainder is capped to the shared cadence.
Usage and break state are preserved. `DeferInteractive(60_000|300_000)` appends
`PersonalDeferred`, reusing the existing snooze remainder but consuming it only
on counted Personal intervals. `Snooze()` retains its all-verified-time clock.
Neither increases allowance; qualifying breaks reset every intervention. Duplicate
deferrals are no-ops while one is active. A dedicated repeat edit changes no
accounting boundary or unfinished break progress. Checkpoint v3 adds the cadence
configuration only; original versions are validated before migration.

The following describes the retained low-level legacy policy (the optional
`Observe` flag defaults to false for original domain clients/probes). The Windows
application supplies true for both styles and does not use automatic grace.

`Normal -> Grace -> Nudging` follows allowance exhaustion. Grace and repeat-nudge
remainders advance **only during counted Personal activity**. Break completion
returns every intervention state to Normal. Snooze may be requested while
exhausted (including Grace); it suppresses reminders while usage continues.
Its remainder advances during all verified, unpaused monitoring intervals,
excluding uncertainty and unexplained downtime. Snooze expiry makes a reminder
due; delivery waits for counted Personal activity. Pause freezes every timer.

Intervals split allowance exhaustion, grace, and snooze
expiry mathematically, including exact boundaries. At most one nudge is emitted
per interval; missed reminders are coalesced, and the next repeat interval starts
at that observation's end. No burst of catch-up notifications is produced.
Effects carry a monotonically increasing sequence and a kind: exhaustion, nudge,
or reset. There is no switching, closure, blocking, tray, dialog, or native
notification implementation. The integration should deliver effects promptly
and suppress obsolete effects if current monitoring no longer supports them.

## Checkpoint and recovery contract

`ICheckpointStore` exposes load and atomic full replacement. The version-1
checkpoint stores configuration, confirmed usage/exhaustion, break progress and
completion latch, pause, intervention timer remainders, effect sequence, and UTC
save metadata. It stores no observation history, foreground identity, clock
epoch, Windows handle, COM object, or absolute reminder deadline. PR 2 adds a concrete
[SQLite implementation and durable session](local-persistence.md) outside Core.

Any accounting or durable state change requires a checkpoint: usage, break/idle
progress or invalidation, reset, exhaustion/nudge, pause/resume, snooze/expiry,
configuration, and explicit reinitialization. Pure duplicate/no-op transitions
need none. The consumer must commit before delivering advisory effects. On save
failure, withhold effects and stop admitting transitions until that exact state
is committed or recovery is performed. Do not continue with a stale checkpoint.

Restoration validates version, configuration, metadata, enums, counter ranges,
and state consistency. It restores totals, pause, exhaustion, configuration,
timer remainders and effect suppression. It discards partial break/idle credit
and observation baselines, never advancing timers for downtime. A completed-break
latch is preserved conservatively until evidence ends that qualifying streak;
legacy idle progress cannot earn new break credit. Historical completed resets
are retained; they are never reconstructed or retrospectively changed.
Use restored configuration first; apply an explicit new configuration through
`ChangeConfiguration` rather than reinterpreting persisted totals.

Checkpoint age alone cannot expire accumulated usage. Old valid checkpoints
retain it; the store must return the latest committed record and report detected
rollback/corruption as unusable. UTC never earns usage or break credit. A future
or default save timestamp is unusable rather than used to infer elapsed time.

Missing, invalid, or incompatible checkpoints produce `RecoveryStatus.Required`.
Monitoring, commands and checkpoint creation are blocked; zero placeholder
counters in this state are **not a restored fresh allowance**. The consumer must
present recovery explicitly. `Reinitialize(configuration)` is an explicit domain
operation available only in that state: it abandons unavailable history, validates
configuration, establishes a new baseline, and requires a new checkpoint. The
constructor is for an explicitly established first run, not an automatic missing-
file fallback. Exhaustion of the effect sequence also requires explicit recovery.

Persist-before-deliver gives at-most-once advisory delivery across restart when
the integration follows the contract: a crash after commit but before delivery
may lose a reminder. Exactly-once notification delivery and preserving changes
never committed before a crash remain outside scope. PR 2 supplies storage
corruption handling and strict serialization. Storage implementations must report load/parse failures,
validate input, and avoid substituting a fresh first run on errors.

## Validation and remaining integration work

Run `./run.ps1 test` for the synthetic Core harness and real local SQLite tests, and
`./run.ps1 format` for SDK formatting verification. Release builds enable SDK
analyzers and warnings-as-errors. Core tests use a fake clock with no elapsed waiting,
OS access, browsers, or UI; generated sequences check invariants as well as exact
boundaries, recovery failures, and the representative 30-minute session.

Existing validation remains `./spikes/VirtualDesktop/run.ps1 test` and
`./spikes/BrowserAttribution/run.ps1 test`. Those are fake, mocked, and synthetic
transport checks, not live Windows compatibility evidence. CI adds a separate
core job, now extended narrowly for persistence, without changing either probe job or feasibility gate.

PR 2 supplies transactional local storage integration. The next slice is adapter
observation/certification, followed by advisory UI. Stable application identity,
foreground/session/idle coverage, missed events, elevated apps, pinned windows,
lock/suspend, shell restart, and monitor configurations require separate live
Windows validation. Neither successful core simulations nor future adapter work
authorizes automatic switching or production website attribution.
