# Owner-evaluation Windows app

This is the first launchable WPF/tray app, not production readiness, certified
accounting or approval for unattended daily use. Explorer recovery [#8](https://github.com/NDePetris/offramp-shift/issues/8)
and sustained accuracy/reliability [#9](https://github.com/NDePetris/offramp-shift/issues/9)
remain unresolved. Automatic switching, window movement and browser attribution
are absent. The shared read-only COM gate accepts exactly **26200.9457/x64** and
**26200.9711/x64**, as approved in merged PR #13. Both worker entry guards and
the read-only ABI remain unchanged. The separate switching gate is unchanged.
See [integration validation and owner checklist](../validation/pr5-app.md).
PR #11 completed the accepted PR 5 owner-evaluation scope; the owner reported
Tests 1–7 PASS and deferred Tests 8–12 / NOT RUN. PR #13 completed the bounded
exact-build read-only compatibility work. Historical evidence and its limitations
remain unchanged. See [PR 6 setup and feedback validation](../validation/pr6-setup-feedback.md)
for the subsequent focused UI changes.

## Ownership

`OfframpShift.ApplicationHost` references ActivityMonitoring, WindowsObservation
and Persistence. PR 8 adds an explicit durable Core usage-reset operation; the
checkpoint format remains version 2. The probe
and WPF app share `MonitoringRuntime`; there is no second estimator/accounting
loop. `Sampler` and anonymous resource instrumentation moved from the probe into
the host. Probe final JSON now places aggregate counters under `diagnostics`,
with native latency/resource aggregates under `diagnostics.providers`.
Native provider aggregates restart when the full provider bundle is explicitly
recreated; they are diagnostic snapshots, not a sustained resource acceptance
measurement. In particular, process CPU counters include process lifetime work,
so do not interpret a post-retry CPU ratio as a fresh independently bounded run.

`MonitoringApplication` owns the durable session, Windows settings and runtime.
`MonitoringController` executes commands and runtime steps on one background
thread. A bounded 16-command queue rejects excess commands. Each turn processes
at most one command, then observation/finalization. Snapshots are immutable,
published after commits, at most every 250 ms apart in ordinary operation, and
immediately after commands. The UI samples the latest snapshot on its Dispatcher;
it never accesses native providers, Core or SQLite. A snapshot older than two
seconds disables accounting controls and is shown as stale. No UI timer earns
usage, break credit or advisory progress.

The extracted runtime retains one-second fixed deadlines, 50 ms event coalescing,
100 ms minimum sampling separation, independent 1,000 ms holdback finalization,
freshness/coverage/generation checks, skipped missed cycles, and post-commit
`AcceptDelivery` rejection handling. Estimated/Unknown aggregate durations remain
distinct from Core-accepted durations. Immediate synchronous commits remain;
they may delay observation and cause rejected/Unknown coverage. No batching,
relaxed freshness, catch-up accounting or replay is introduced.

## Setup, restoration and commands

First setup creates a CertifiedOnly checkpoint and no native providers before
explicit experimental-observation consent. Assign Personal/Work from fresh
read-only GUID observations, then explicitly Start monitoring. Start commits
BestEffort through `ChangeConfiguration`; it never labels live evidence Confirmed.
Saved enabled monitoring resumes on later application launches only with valid
settings/consent/build and a BestEffort checkpoint. Both GUIDs must still exist
for the estimator to account. Saved Pause is preserved. No Windows login startup
registration is added. Startup and recovery require fresh endpoints; downtime
and unfinished continuity receive no credit.

The tray can assign the current desktop without activating the main window.
Users change desktops themselves in Task View. Names/indices never identify a
role. A successful reassignment disables monitoring, reopens the existing durable
session to discard unfinished continuity, and requires explicit Start again.
Duration edits retain unrelated configuration and rebuild estimation only after
the committed change. Pause/Resume use durable Core commands and fresh baselines.

PR 6 projects a four-part setup checklist from immutable host snapshots: consent,
Personal assignment, Work assignment, and explicit Start. Home expands it when
setup needs attention; Settings retains the details. Individual assignment states
distinguish unassigned, freshly validated, missing/invalid, and temporarily
unverifiable identities without displaying GUIDs. Consent and assignment controls
use host eligibility plus UI snapshot freshness; commands still revalidate on the
owner thread. Names/indices never substitute for GUIDs.

Commands return `HostCommandResult` with explicit success and a user-facing message;
nonempty successful assignment messages cannot be mistaken for failures. Provider
recreation failure is unsuccessful even when construction catches its exception
internally. Successful recreation only claims that providers restarted, with fresh
evidence still required. The queue, provider cooldown and automatic recovery policy
are unchanged.

Tray failures retain one in-memory message behind **Action needs attention…** and
mirror the explanation into Home/Settings. A native NotifyIcon balloon advises of a
new failure without opening/activating the main window. Windows can suppress the
balloon; the menu remains the dependable discovery path. Choosing the menu item or
an unambiguous error balloon explicitly opens a native keyboard/screen-reader-accessible
details dialog;
OK dismisses it. Repeated identical failures for the same command do not repeat the
balloon. A successful retry of that command clears its failure; other successes
stay quiet. This retains only the latest failure, not an action history, durable
notification store or toast framework. Real shell/Narrator behavior needs owner
smoke validation; automation/property checks alone do not establish accessibility.

Retry monitoring replaces read-only providers after the existing ten-second
cooldown. Shell generation changes retain the existing one replacement attempt
per generation. Failed construction/cleanup stops accounting, with no unbounded
retry. Manual retry is not a repair of issue #8. Retry save is separate: it retries
the exact pending checkpoint, including acknowledgment-loss handling, then resets
estimation without delivering old effects. Pending state is not exposed as saved
accounting. RecoveryRequired alone offers explicit reset through the existing
archive protocol. Incompatible/Blocked states never offer destructive recovery;
Blocked permits explicit reopen after correcting clock/access/ownership.

## Break reminders and manual usage reset (PR 8)

Break reminders are opt-in and default off. They consume only newly committed
Core `Nudge` effects after estimator delivery acceptance. Core's grace and repeat
cadence continues to use eligible Personal usage, not elapsed UI time. Preference
changes patch only Windows settings; they preserve usage, configuration and
unfinished break progress. Enabling the preference does not emit an immediate
notification from saved exhaustion/advisory state.

The host has one process-local reminder slot, consumed atomically by the existing
Dispatcher refresh before requesting a balloon. It expires within two seconds
of creation, bounded further by the original observation/tick freshness; snapshot
publication does not renew it. Runtime rechecks current evidence after a commit,
including signals received during the save. Paused, idle, locked, Work/Other,
unhealthy/stale/unknown context, changed foreground identity and missing desktop
bindings suppress delivery. Commands, resets, provider changes, storage failure,
retry, restore and shutdown discard pending reminders. Neither effects nor the
mailbox are replayed from storage, counters or presentation refreshes.

The existing `NotifyIcon.ShowBalloonTip` mechanism remains best effort. A request
returning without exception is not evidence of display: Windows may suppress or
display a balloon late. The owner explicitly accepted this limit for PR 8. There
is no guaranteed display freshness, native notification backend, toast framework,
independent notification timer or delivery retry. Only one balloon request is
outstanding until a native close/click callback; intervening events are discarded.
Reminder clicks do not open the main window or error details. Retained actionable
errors suppress reminders. An
outstanding reminder is not replaced by an error balloon because native callbacks
have no message identity; the new error remains accessible through **Action needs
attention…** and Home/Settings. After a balloon closes/clicks, ordinary error
balloon routing is available again. A suppressed balloon may provide no lifecycle
callback, so the retained menu remains the dependable error path.

**Reset usage…** is a separate confirmed Settings action for Ready storage,
including while paused or observation is unavailable. It clears usage, estimated
usage, exhaustion, break progress/completion, idle/uncertainty and advisory/snooze
state. Configuration, Pause, Windows settings and monotonic effect sequence are
preserved. Core establishes a command boundary and the runtime requires fresh
endpoints; uncertain time earns no credit. Success is reported only after a
durable save acknowledgment. A failure retains the exact PendingCommit checkpoint
and hides unacknowledged state; Retry save keeps existing acknowledgment-loss
handling and returns no delayed effects. This does not archive/delete the database
or replace the separate Recover accounting protocol. See the
[PR 8 validation and short owner smoke procedure](../validation/pr8-reminders-reset.md).

## Windows settings

`%LOCALAPPDATA%\Offramp\Shift\windows-settings.json` is independent of
`checkpoint.db`. It stores schema/provider/build, nullable role GUIDs, experimental
consent, enabled-monitoring intent, theme, close behavior and default-off break
reminder preference. Settings version 2 strictly migrates the exact version-1
shape in memory, preserving all existing fields and setting reminders off. Load
does not rewrite the file; the next explicit atomic save writes version 2.
Version 2 requires the reminder field; version 1 rejects it as unknown. It contains no
Core totals/configuration, foreground identities or activity/content history.
The codec rejects duplicate/unknown/missing fields, unsupported versions and
invalid values. Saving flushes a unique same-directory temporary file and uses
atomic replacement (or rename for creation). Active settings change only after
success; abandoned temporary files are never promoted. Corrupt settings require
explicit repair with a flushed preserved original. Future versions cannot be
repaired by this app. Permission failures are not treated as missing settings.

There is no cross-file transaction. Accounting requires all prerequisites;
interrupted setup can leave enabled intent without BestEffort, but never enables
accounting from that intent alone. A failed Core commit retains PendingCommit.
Missing/corrupt Windows settings cannot reset accounting storage. Original
checkpoint recovery remains unchanged. Flush/rename guarantees depend on the
local filesystem/hardware; this is not a power-loss certification.

## Executable, UI and lifecycle

The app is an x64 framework-dependent WPF WinExe. Its STA entry point dispatches
`--desktop-worker` before WPF, singleton or storage initialization. The existing
same-executable redirected-stdio read-only worker protocol, process isolation,
COM declarations and two-second worker response deadline remain unchanged.
Non-COM fault modes test the actual apphost executable's redirected streams.

A current-user restricted activation pipe carries one activation byte; a named
mutex coordinates the interactive instance. No network endpoint exists. The
checkpoint lease remains the final exclusion against another storage owner.
Activation obeys ordinary Windows foreground restrictions; it does not move
windows or switch desktops. First close explains tray continuation versus Exit;
the chosen behavior is saved. Tray initialization failure prevents hiding the UI.
The first-close choice uses a dedicated `ChangeCloseBehavior` command that patches
only that preference against the owner's current settings. The window hides/exits
only after a successful result. A rejected or failed save leaves it open with an
explanation: close again to retry/choose differently, or explicitly Exit without
saving a preference. Cancellation saves nothing. Theme, consent, assignments and
monitoring intent are preserved. PendingCommit/Exit and worker cleanup paths remain
separate and unchanged.
Exit stops the owner and disposes providers/session before removing the tray.
Pending-save Exit explicitly offers retry, cancel or potential loss of the pending
change. Windows session ending requests best-effort stop without postponing logout.

Existing in-process native calls cannot be forcibly canceled safely. Shutdown
reports delayed/failed cleanup rather than claiming completion. No new process
isolation design or kill-on-close job was added: that would need the owner's
separate boundary review. Parent death normally closes worker stdin; a helper
already blocked inside COM is an existing unresolved crash-cleanup limitation.
Committed totals restore after a crash; pending changes may not survive and
effects are never replayed.

Home/Settings use native WPF controls, Fluent framework resources and system
semantic colors. The framework's evaluation-marked ThemeMode API has a local
documented diagnostic opt-in; warnings remain errors elsewhere. Light/dark/system
selection, dynamic native resources, labels/access keys, automation names and
wrapping/scrolling support accessibility; real Narrator/high-contrast/DPI acceptance
is separate. Dashboard/tray distinguish primary availability/pause from the saved
exhaustion/advisory state. No toast notification infrastructure is added.

PR 7 adds read-only native tray rows for current context, saved remaining allowance
and saved qualifying break progress/requirement, plus a concise status tooltip.
The dashboard's existing refresh/freshness decision supplies an immutable tray
projection; no UI time is credited or extrapolated. Minute-level labels update
only when their text changes. Paused/unavailable/stale/setup states distinguish
retained saved accounting from current monitoring; unavailable/stale context is
Unknown and unacknowledged storage values are never presented as saved. Existing
actions and PR 6 feedback are preserved. See the [short PR 7 owner smoke checklist](../validation/pr7-tray-summaries.md).

Quiet Road palette constants are retained without invented semantic colors.
The owner-supplied v0.1 canonical family assets are deployed unchanged in
`src/OfframpShift.App/Assets/Offramp`, with source hashes/provenance. The executable,
window and tray use the supplied family ICOs; the header selects the primary or
reversed PNG against the effective native light/dark background. Decorative header
art is hidden in high contrast; the native text heading remains. This provisions
the family symbol without approving a differentiated Shift icon or the handoff's
proposed semantic mappings. See the asset README and validation record.

No telemetry, runtime network calls, activity history or automatic diagnostics
files are added. Licensing/distribution remain undecided.
