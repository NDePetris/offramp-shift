# Core monitoring engine (PR 1)

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
An integration must serialize the whole observe/command, checkpoint, and effect
delivery transaction, not only individual coordinator calls. Do not run a second
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

Only confirmed, active, input-active, classified counted Personal intervals
consume usage. Work, Other/excluded activity, idle, lock, uncertainty, suspension,
and explicit pause consume none. Usage survives application changes, short
breaks, restart, and calendar rollover. Allowance exhaustion remains latched.

Breaks are independent from usage:

- Confirmed Work and confirmed lock earn credit immediately. Lock needs known
  desktop scope and monitoring confidence, but not foreground application/input.
- Confirmed idle outside Work earns credit only after the continuous idle
  threshold. The first 60 seconds earn no retroactive credit and consume no usage.
  Work earns break credit even during its initial idle qualification period.
- Counted Personal and neutral/excluded activity end the break. Idle before its
  threshold also ends a partial break; qualified idle, Work, and lock can connect
  when certified coverage is uninterrupted.
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

`Normal -> Grace -> Nudging` follows allowance exhaustion. Grace and repeat-nudge
remainders advance **only during counted Personal activity**. Break completion
returns every intervention state to Normal. Snooze may be requested while
exhausted (including Grace); it suppresses reminders while usage continues.
Its remainder advances during all verified, unpaused monitoring intervals,
excluding uncertainty and unexplained downtime. Snooze expiry makes a reminder
due; delivery waits for counted Personal activity. Pause freezes every timer.

Intervals split allowance exhaustion, grace, idle qualification, and snooze
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
epoch, Windows handle, COM object, or absolute reminder deadline. No concrete
persistence implementation is included.

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
requalifying idle after restart does not rearm a completed-break reset.
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
may lose a reminder. Exactly-once notification delivery, storage corruption
handling/serialization, and preserving changes never committed before a crash
are outside this PR. Storage implementations must report load/parse failures,
validate input, and avoid substituting a fresh first run on errors.

## Validation and remaining integration work

Run `./run.ps1 test` for the package-free synthetic harness, and
`./run.ps1 format` for SDK formatting verification. Release builds enable SDK
analyzers and warnings-as-errors. Tests use a fake clock with no elapsed waiting,
OS access, browsers, or UI; generated sequences check invariants as well as exact
boundaries, recovery failures, and the representative 30-minute session.

Existing validation remains `./spikes/VirtualDesktop/run.ps1 test` and
`./spikes/BrowserAttribution/run.ps1 test`. Those are fake, mocked, and synthetic
transport checks, not live Windows compatibility evidence. CI adds a separate
core job without changing either probe job or feasibility gate.

The next slice is adapter observation/certification and transactional local
storage integration, followed by advisory UI. Stable application identity,
foreground/session/idle coverage, missed events, elevated apps, pinned windows,
lock/suspend, shell restart, and monitor configurations require separate live
Windows validation. Neither successful core simulations nor future adapter work
authorizes automatic switching or production website attribution.
