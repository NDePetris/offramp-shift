# Owner-evaluation Windows app

An explicitly armed, process-local [reminder authorization capture](../validation/interactive-reminder-authorization-capture.md)
can retain one real initial authorization check from the existing isolated worker.
Its categorical result is exposed in Diagnostics; it does not issue a host command,
save preferences, repeat membership queries or change authorization/presentation.
The `--open-diagnostics` launch option opens this view temporarily without arming
capture or changing its saved visibility preference.

This is the first launchable WPF/tray app, not production readiness, certified
accounting or approval for unattended daily use. Explorer recovery [#8](https://github.com/NDePetris/offramp-shift/issues/8)
and sustained accuracy/reliability [#9](https://github.com/NDePetris/offramp-shift/issues/9)
remain unresolved. Automatic switching and production browser attribution remain
NO-GO; no window movement is implemented. PR 10 adds an explicitly clicked, separately
consented Switch to Work action on 26200.9457/x64 and the 26300/x64 family for
controlled owner evaluation. Switching on 26200.9711/x64 remains disabled.
See [explicit switching](explicit-switching.md) and
[the owner procedure](../validation/pr10-explicit-switching.md).
The owner accepted limited 26300.9711/x64 standalone evidence and approved
integrated testing without further standalone repetitions. The shared read-only
gate now accepts exact **26200.9457/x64**, **26200.9711/x64**, and numeric
**26300.UBR/x64**. OS and process must both be x64. The read-only ABI, isolation
and validation remain intact. See [the decision and integrated procedure](../validation/pr10-integrated-26300.md).
See [integration validation and owner checklist](../validation/pr5-app.md).
PR #11 completed the accepted PR 5 owner-evaluation scope; the owner reported
Tests 1–7 PASS and deferred Tests 8–12 / NOT RUN. PR #13 completed the bounded
exact-build read-only compatibility work. Historical evidence and its limitations
remain unchanged. See [PR 6 setup and feedback validation](../validation/pr6-setup-feedback.md)
for the subsequent focused UI changes.

## Ownership

PR A adds a compact timer-first Home and Home/Settings overview/detail shell.
`DashboardModel` owns the shared in-memory committed presentation pair used by
Home/tray; the owner publishes a process-local `AccountingPresentationEpoch` before
accounting replacement/reset/reassignment/reopen/retry boundaries. Only Ready
state/configuration pairs populate the cache; epoch changes and terminal ownership
invalidate obsolete values. The ordinary UI command coordinator provides pending,
delayed and explicit-result feedback without replacing host or nudge transactions.
PR B supplies protected Settings drafts/conflicts and optional Diagnostics. See
[the PR A contract and validation](../validation/pr-a-timer-first-home.md).

PR C groups the existing committed timer presentation in a sage hero, with a
static frozen QuietRoad drawing also used by the separate interactive reminder.
The shared palette follows light/dark themes; contrast fallback uses system
brushes and removes the scene. No decorative clock or accounting state is added.
The reminder's Open Offramp Shift action invokes the existing main-window open
route and leaves the authorized reminder open, avoiding a dismissal deferral.
Existing authorization, add-time, closing and focus safeguards are unchanged.
See [PR C validation and screenshots](../validation/pr-c-visual-polish.md).

`OfframpShift.ApplicationHost` references ActivityMonitoring, WindowsObservation
and Persistence. PR 8 adds an explicit durable Core usage-reset operation; the
checkpoint format is now version 4 for timed Pause (v3 added PR 9 cadence). The probe
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

## Interactive nudges (PR 9)

Windows settings v5 preserves v3's Off, Windows notification and explicitly selected
Interactive window. v1 migrates to Off; enabled v2 reminders migrate to Windows
notification, never interactive focus. Both enabled styles use zero initial grace
and one shared durable repeat interval (1–60 minutes, default one minute), advanced
only by counted Personal usage. The v3 `InteractiveRepeatNudgeMs` serialized name
is retained; its meaning is now shared. Existing grace is retired on the next
eligible Personal interval, and old repeat remainders are capped to this interval.
Settings edits never replay effects. A cadence-only edit preserves unfinished break progress.
`PersonalDeferred` uses the existing snooze remainder for Personal-only extra
time; legacy `Snooze()` retains its all-verified-time clock. Checkpoint v3 strictly
migrates original v1/v2 layouts without replay or downtime credit.

`NudgePresenter` owns one unowned modeless WPF window. Only new committed mailbox
events authorize show/refocus. Documented read-only window-desktop checks run in
the existing isolated worker. Initial authorization consumes the original fresh
committed event once and creates one nonrenewable, process-local presentation
transaction, bounded to two seconds from acceptance. After layout/Show, completion
captures NEW session/input/foreground/desktop evidence and requires positive strict
membership before one ordinary activation attempt. It does not reuse the original
accounting event's TTL or require the estimator to have recovered from the nudge's
own foreground event. All signals still reach accounting normally.
Acceptance happens BEFORE creating/initializing the nudge HWND. One provisional
foreground HWND may be held privately during creation, never trusted until it is
bound to the exact UI-owned candidate at completion. Every observed foreground
must be original or that candidate; a second distinct provisional HWND revokes it.
External foreground, relevant destruction, session/desktop/power,
sequence/health faults and commands revoke it. Fresh context and shell checks are
independent. The UI also checks its current transaction ID, context and foreground.
Only the nudge is positioned on the foreground
monitor using target DPI. The original foreground's membership/current Personal
desktop/session are strictly verified before construction. The visible candidate's
own desktop ID is positively verified after Show and before focus. Production no
longer needs a hidden-HWND missing-registration exception. Reused windows follow
the same strict completion path.
Placement measures content plus native frame, not the collapsed hidden Window.
The temporary documented WS_EX_NOACTIVATE style prevents WPF's nested DPI-change
positioning from implicitly activating the HWND; its exact original style is
held through Show and queued DPI/layout settlement, then restored before strict
completion. Failure to apply/restore suppresses
presentation. The app-test executable shares the production PerMonitorV2 manifest.
Home retains one identity-free in-memory reminder stage and bounded phase durations
for diagnosis. The real-host opt-in diagnostic replaces the old standalone checks.
Wrong/unknown membership suppresses activation; no switching
or virtual-desktop movement API is added. Continued visibility tolerates its own
foreground transition while requiring fresh healthy Personal/session evidence.
Context/storage/observation changes close it without granting extra time. User
X/Escape/Alt+F4 cancel ordinary Closing and await the same durable one-minute
deferral as Add 1 minute, retaining the HWND until confirmed success. One pending
submission owns dismissal; automatic context closure/shutdown never submits one.
Eligible failures leave actionable inline feedback and one local failure report;
obsolete context-cancellation failures are suppressed, storage failures stay explicit.

Issue #22 adds one best-effort `HWND_TOP` raise with `SWP_NOMOVE | SWP_NOSIZE |
SWP_NOACTIVATE`, only after strict completion and the UI's final transaction check.
The UI rechecks the same HWND, transaction, expiry, context and actual foreground
again before its single foreground request; no dispatcher yield separates these
operations. No topmost band, activation retry or refresh-triggered raise is used.
Raise failure alone leaves an otherwise eligible reminder available. A passive
tray hint points to the explicitly enabled taskbar button only while an eligible
reminder is actually open; closure immediately clears it and refresh rechecks
eligibility. The hint has no action and sends no notification.

The latest presentation result retains Show/layout, authorization, raise and
foreground-request outcomes, with identity-free native observations after Show
and immediately after the request (foreground role, visible/minimized/topmost
state, original-monitor match and DPI). Diagnostic exceptions become Unknown;
these observations never authorize presentation or affect accounting. There is
no activity history, HWND/process identity output, deactivation tracking or polling.
Successful native calls and native visibility do not prove unobscured pixels or
lasting focus. See [issue #22 validation](../validation/issue22-reminder-visibility.md).

See [PR 9 behavior, boundaries and owner checklist](../validation/pr9-interactive-nudges.md).
Real-host nonactivating placement/confirmation passed on the owner's portrait
2160-pixel-wide secondary monitor at 150%, build 26200.9711/x64. Actual focus and
extension acceptance were subsequently **owner-reported PASS on `729ea52`** with
3440×1440/100% primary and 4K portrait/150% secondary. On `d0b3fdb`, the owner
confirmed both-monitor presentation, visibility/focus, Add 1 minute and removal
of the X-dismissal error, accepting PR 9 for continued owner evaluation. Separate
live Escape/Alt+F4 acceptance is unrecorded; automated coverage is not live proof.
The owner also observed approximately two minutes between nudges despite a
one-minute extension while usage appeared to advance. Its cause is unresolved
and accepted for now, not a proven Core regression or verified live cadence.
See the validation closeout for the #9 follow-up recommendation. No accounting
or timer policy changes are inferred. Any presentation-caused desktop switch
remains a blocker; existing NO-GO gates, read-only build restrictions and recovery remain.

## Personal usage accounting (PR 11)

Verified, unlocked, otherwise eligible Personal desktop presence consumes usage,
including video playback or an unattended desktop beyond the inactivity and break
durations. Existing application rules still apply. Only verified Work/lock time
qualifies for a break; idle on Personal or Other never qualifies. Paused, unknown,
unavailable, unsupported and suspended time earns neither credit. Continuity,
fresh baselines, holdback and all native evidence gates remain unchanged.

Idle Personal also consumes Personal-only extensions and advances Core repeat
cadence. The configured **Reminder inactivity threshold** still suppresses
presentation; committed effects can be suppressed and never replay on input
resumption. No new timers, accounting owner or focus authorization are added.
Saving durations retains its existing unfinished-break invalidation behavior.

Checkpoint v3, SQLite schema 1 and Windows settings v5 are unchanged. Existing
usage, exhaustion, extensions and completed-break latches restore as before;
unfinished break/idle continuity is discarded. Legacy idle fields remain readable
but cannot grant break credit. No history is rewritten or downtime charged.
See [PR 11 validation and owner procedure](../validation/pr11-personal-usage.md).

## Break reminders and manual usage reset (PR 8 baseline)

Break reminders are opt-in and default off. They consume only newly committed
Core `Nudge` effects after estimator delivery acceptance. PR 9 supersedes this
baseline's automatic grace and separate cadence with the shared timing above. Preference
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
reminder style. Version 5 retains ReminderStyle, SwitchingConsent and the exact
SwitchingConsentBuild field, and adds explicit SwitchingConsentFamily (blank or
26300). Strict v1–v4 migrations leave switching off and preserve other preferences;
v1/disabled v2 becomes Off and enabled v2 becomes Windows notification. Load never
rewrites the file; the next explicit atomic save writes v5. Family consent granted
through Settings survives routine 26300 UBR updates. Observation setup likewise
retains consent within that family, while keeping the full saved build string.
Build-family changes, explicit setup review and role reassignment revoke switching
consent. On a scope change at startup, revocation is saved before opening accounting;
save failure aborts startup. See the switching architecture for exact scope rules.
Consent never grants capability outside the approved policy. It contains no
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

The app is an x64 framework-dependent WPF WinExe. `run.ps1 app` builds and launches
the generated `OfframpShift.App.exe` apphost directly; the .NET runtime remains
required. The apphost embeds the Offramp Shift title/product and application
description. This removes the explicit `dotnet OfframpShift.App.dll` launch path,
but does not guarantee Windows notification attribution: the unpackaged app still
uses legacy NotifyIcon balloons and system-selected shell identity. No notification
registration, installer or new framework is added. Its STA entry point dispatches
`--desktop-worker` before WPF, singleton or storage initialization. The existing
same-executable redirected-stdio worker adds only the read-only membership request.
Process isolation, private observation COM declarations and the two-second worker
response deadline remain unchanged.
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

PR B replaces the temporary Settings editors with protected, explicit-save drafts
and command-owned expected-value checks. These checks run after existing safety
invalidations on the serialized owner and return a structured conflict without
mutating configuration/accounting. Back, close-to-tray and Exit protect drafts;
first-close, exact pending-checkpoint retry and shutdown retain their contracts.

Optional Diagnostics is a separate disposable nonmodal view of the same
DashboardModel/committed projection and CommandCoordinator. It has no independent
monitoring state or polling. Its default-off preference uses the separate,
strictly versioned app-owned `ui-preferences.json`; malformed/future files are
preserved. Successful opt-in only enables Open Diagnostics; startup never opens
the companion. Recovery stays available in Advanced with Diagnostics off. See
[PR B validation and owner checklist](../validation/pr-b-settings-diagnostics.md).

No telemetry, runtime network calls, activity history or automatic diagnostics
logs are added. Licensing/distribution remain undecided.

## Timed Pause (issue #26)

Home and tray share one accessible Pause dialog with duration presets, an explicit
local date/time (including future dates), and indefinite Pause. Confirmation
captures one UTC deadline. Invalid DST times and past selections are rejected;
ambiguous times require an explicit occurrence. No countdown is displayed.

The quick durations are four compact radio selections styled as native toggle
buttons, sharing one exclusive group with custom and indefinite choices. Only
the separate Pause confirmation submits a command. Custom input uses the native
DatePicker plus hour/minute/AM-PM selectors, including all 60 minutes. It defaults
to the next future quarter-hour with date rollover; DST ambiguity still requires
an explicit occurrence. The zone name omits the fixed standard-time offset.
Content-sized height keeps presets compact and expands for custom input, with
scrollable fields and pinned validation/actions. Working-area height is capped
using the current monitor and DPI. These changes affect UI selection only;
UTC conversion, checkpoint v4, expiry and recovery contracts remain unchanged.

MonitoringApplication reconciles deadline expiry before provider-dependent work,
at startup and after save retry/reopen. No dedicated timer service is introduced.
Manual Resume and expiry require ready durable storage, independently of desktop,
activity, lock or provider availability. Ending Pause is separate from accounting
availability: setup/build/consent/binding gates and fresh estimator evidence remain
required. Runtime continuity and pending reminder/switch authorization are reset.

Pending/blocked commits never publish speculative resumption. An expired confirmed
Pause whose clear cannot be saved has actionable storage feedback. UTC is the
authority: clock corrections affect expiry; timezone changes only affect display.
Sleep/exit do not extend deadlines; the next runnable owner turn reconciles them.
Future checkpoint save timestamps retain conservative clock blocking. No startup
registration, retroactive accounting, break credit or reminder replay is added.
See [tests and minimal owner smoke](../validation/issue26-timed-pause.md).
