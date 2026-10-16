# PR 9 — interactive nudges

Owner evaluation only. Initial owner smoke **FAIL**: Interactive window never
appeared; legacy notifications still worked, including their expected grace.
At `56f23ce`, the owner's primary monitor **PASSED** and secondary portrait/150%
monitor **FAILED**. The third correction below reproduces and addresses the
production lifecycle rejection; real-host nonactivating secondary presentation
and strict confirmation now pass. Full owner focus/extension retest remains pending.
Fullscreen and Narrator acceptance remain **NOT RUN** by the agent.
Automated tests use synthetic evidence, disposable SQLite, owned hidden HWNDs
and fake placement/focus adapters. They do not establish live shell reliability.

## Timing and persistence

Windows settings v3 replaces the Boolean preference with Off, Windows notification
and Interactive window. v1 migrates to Off; v2 enabled/disabled migrates to Windows
notification/Off. Original shapes are validated strictly; load never writes or
enables interactive focus. The next explicit atomic save writes v3. The owner has
now explicitly approved replacing the previous notification grace/repeat policy.

Both enabled styles prompt at the durably accepted exhaustion boundary, without
automatic grace, subject to existing sampling, holdback and freshness limits.
Add 1 minute, Add 5 minutes, X, Escape and Alt+F4 use a distinct durable deferral.
Extensions and repeats advance only through counted Personal usage. Work, lock,
idle, neutral, Unknown, Pause and downtime consume none; a qualifying break
clears the intervention normally. Usage continues; extra time grants no allowance.
Legacy `Snooze()` retains its all-verified-time clock.

Core stores one shared reminder repeat interval: default 60,000 ms, restricted
to 1–60 whole minutes. Its dedicated edit preserves unfinished break progress,
usage and Pause. Style and cadence have separate saves in their existing stores;
no cross-file transaction is implied.

Checkpoint v3 retains the original `InteractiveRepeatNudgeMs` field and shape;
the existing value now applies to both styles. No destructive reset/version bump
is needed. Old Grace state becomes due on the next eligible Personal interval;
old Nudging remainders are capped to the shared interval. Neither load nor a
Settings save emits historical effects. The appended `PersonalDeferred`
intervention value distinguishes the clocks using the existing snooze remainder;
there is no new state field or absolute deadline. v1/v2 layouts and original
invariants are validated before transactional migration. Migration preserves
durable state/metadata; normal runtime restoration still abandons unfinished
continuity, as before. Exact pending-commit/acknowledgment-loss retry remains;
effects are never replayed. Older applications reject v3; downgrade is unsupported.

## Presentation and safety

The Dispatcher owns at most one unowned modeless nudge, created without activation,
using native Fluent/system colors and supplied family artwork. Neither button is
a default Enter action. Failed deferrals remain explicit in the open window,
Home/Settings and retained tray feedback. X/Escape still dismiss during submission.
Programmatic closure grants no time.

The existing transient mailbox carries an effect sequence, runtime epoch,
observation generation and original expiry. Consumption never renews it. New
committed events alone may show/refocus; ordinary refresh only updates/closes.
Initial authorization consumes that event once and creates one process-local,
single-use presentation transaction with its own nonrenewable two-second limit.
Completion requires new healthy Personal/session/input evidence and strict desktop
membership. Within this transaction only foreground events for the original and
owned candidate are allowed; external context changes, commands, health faults
and relevant destruction revoke it. Every signal still invalidates accounting
normally. The Dispatcher checks the current transaction ID/context/foreground
before focus, without requiring the original accounting generation/TTL to survive
its own WPF transition. Runtime epochs prevent
recovered effect sequences colliding with earlier presentation suppression.

Continued visibility separately requires healthy active Personal context, so the
nudge taking focus does not dismiss itself. Its identity is classified normally:
CountByDefault counts it unless excluded; BlocklistOnly counts it only if included.
It becomes idle normally. No synthetic activity or UI countdown advances Core.
Pause, Work/Other, lock, unavailable/stale evidence, reset, style change, storage
failure, owner stop and exit close the nudge without granting extra time.

Documented read-only `IVirtualDesktopManager` checks run through the existing
isolated worker. Initial acceptance verifies the original foreground's membership,
Personal desktop, session and shell BEFORE WPF construction. After nonactivating
Show, the candidate's own desktop ID must positively match Personal under fresh
transaction completion checks before activation. Production no longer needs the
historical hidden-HWND missing-registration exception. Reused windows follow the
same strict completion path. Failures and known wrong desktops are rejected.
There is no registration retry or focus retry. The public declaration
ends before MoveWindowToDesktop; the private observation ABI, worker deadline,
exact-build guards and recovery policy remain unchanged. No COM runs on the UI.

Only the nudge is positioned. MonitorFromWindow selects the largest foreground
intersection; the working area and nudge's own target DPI determine placement.
Negative origins are supported; an insufficient minimum width fails closed.
Display reconfiguration closes the nudge; a later eligible event can replace it.
An existing wrong-desktop nudge is closed, never moved or activated.

Each eligible event makes at most one ordinary foreground attempt. Focus denial
leaves the current-desktop window available through ordinary Windows interaction.
Unknown membership/placement suppresses the event. Fullscreen visibility/focus
are best effort. There is no input injection, topmost toggling, foreground-lock
manipulation or activation retry. Physical monitor video-input selection is ignored.

Windows has no atomic check-desktop-and-activate operation. Revalidation reduces
but cannot certify the absence of shell races. **Any observed desktop switch
caused by presenting the nudge is a blocking failure.** Never bypass it with
switching, virtual-desktop window movement or a broader compatibility allowlist.

## Automated checks

Run all seven repository-prescribed test/format commands and record final results
in the PR. Focused tests cover both clocks, migrations, exact retry, fresh committed
events, command authorization, context/generation suppression, single-window reuse,
dismissal, accessibility properties and physical placement arithmetic. Synthetic
light/dark rendering is separate from live focus or Narrator acceptance.

Local automated validation on October 15, 2026, with pinned .NET SDK 10.0.401:

| Check | Result |
|---|---|
| `./run.ps1 test` | PASS — 81 Core, 77 Persistence, 74 host, 33 app groups |
| `./run.ps1 format` | PASS; final app/app-test edits also separately verified |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS — 58 fake-provider cases and three owned non-COM worker checks |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS — 2,053 policy/protocol assertions, five mocked extension tests, synthetic transport |
| `./spikes/WindowsObservation/run.ps1 test` | PASS — 41 synthetic/local-SQLite groups |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS — 14 synthetic/owned non-COM groups; unsupported membership requests also checked |
| `./spikes/WindowsObservation/run.ps1 format` | PASS |
| Final app build/tests and `--render` | PASS — 33 groups; light/dark nudge and reminder Settings captures inspected |
| Scoped Impeccable detector / Git whitespace checks | PASS |

Builds used warnings as errors. Checks ran outside the tool sandbox after its
MSBuild project-reference evaluation failed without diagnostics. An initial
standalone render lacked DOTNET_ROOT; its two verified test-only apphost children
were stopped, then rendering passed with the pinned runtime configured. No running
owner app was stopped and no live monitoring, focus or desktop exercise was run.
The compatibility guard's forbidden-native test stubs were extended only to compile
and reject the new linked read-only membership branch; live probe behavior is unchanged.

## Short owner smoke checklist

Record commit, exact supported Windows build/x64, monitor layout and scaling.
Record PASS/FAIL/NOT RUN; note original durations before temporarily changing them.

1. Confirm an existing enabled v2 reminder preference becomes Windows notification,
   with the newly approved immediate/shared timing. Do not edit settings under a running app.
2. Explicitly save Interactive window and temporarily shorten the allowance. Use
   Personal through exhaustion. Confirm the nudge, expected foreground monitor,
   two approved buttons, no automatic grace, and no desktop switching. Record
   focus denial separately from a successful activation.
3. Exercise Add 1 minute, Add 5 minutes and X/Escape. Briefly use Work and return
   before a qualifying break: extra time must remain available for Personal use.
   Verify ordinary exit/relaunch preserves it without downtime credit.
4. Leave a nudge open, focus another Personal application, and confirm the next
   eligible event targets that same window without continuous focus theft. Check
   Tab, access keys, Escape and readable focus; briefly check Narrator if practical.
5. Verify Work, lock and Pause suppression, then an ordinary qualifying-break
   reset. Check placement/scaling on the owner's normal arrangement; no broad matrix.
6. Restore temporary settings. A presentation-caused desktop switch is FAIL and
   blocks acceptance; record the exact sequence and configuration.

PR 5 Tests 8–12 remain deferred / **NOT RUN**. Explorer recovery (#8), sustained
accuracy/reliability (#9), native notification branding/placement (#17), PR 10
switching and distribution/licensing remain outside scope. Automatic switching,
virtual-desktop window movement and production browser attribution remain **NO-GO**.
No telemetry, activity history or remote service is added.

## First October 16 correction: missing nudge (historical)

The evidence in this section predates the owner's placement failure and the
shared notification timing decision. Its no-timing-change statement describes
that first correction only; the current policy is documented above.

Two independent blocking conditions were reproduced on Windows build
**26200.9711/x64**, pinned SDK 10.0.401, using an owned test HWND and the existing
isolated worker. No user accounting/settings were opened or changed.

1. For the new hidden HWND, IsWindowOnCurrentVirtualDesktop returned S_OK/true,
   but GetWindowDesktopId returned **0x8002802B (TYPE_E_ELEMENTNOTFOUND)** with
   no identity. The old worker treated this as rejection, before Show.
2. Window.Measure on the never-shown WPF Window returned **DesiredSize.Height=0**,
   while its content measured **389.5033 DIPs**. Old positioning would reject
   zero height even if authorization passed. Earlier fake placement tests and
   content-only renders did not exercise this path.

The correction separates permission to first-show without activation from strict
permission to request focus, retaining all original context/foreground/freshness
checks. Positioning measures visible content and adds DPI-correct native frame
dimensions; it does not measure the collapsed top-level Window. No Core,
notification timing, extension semantics, settings or checkpoint migration changes.

A single bounded nonactivating registration exercise then reported:
hidden strict check CandidateIdentityUnavailable; preparation Allowed; positioning
true; post-Show strict check Allowed; foreground and desktop unchanged. The owned
window was closed automatically. **No activation was attempted.** This is real
read-only COM/owned-window registration evidence on that exact build, not an
owner-flow, focus, multi-monitor/scaling or accounting PASS. The running owner
app remained running; builds use ignored isolated artifacts to avoid its file locks.

The stability claims above are before/after identity equality checks, not a
certified continuous desktop/focus history. No identities were printed or retained.

Correction validation: all seven established commands **PASS**, with final affected
host/host-test formatting separately rechecked after the publication regression.
Final application tests: **81 Core, 77 Persistence, 78 host, 36 app groups**; builds
retain warnings as errors. Compatibility includes seven pure candidate-membership
decisions as well as the existing unsupported-build/architecture and owned non-COM
checks. No automated test runs either opt-in live diagnostic.

Home now retains only the latest process-local reminder stage: whether a committed
nudge was offered, consumed, rejected before/after Show, failed positioning, or was
shown with focus accepted/denied. Worker failure categories are discrete codes,
not foreground/window/desktop identities. This status is not written to storage,
sent anywhere, or retained as a history. If the short retest fails, report just the
Home **Last reminder** line and monitoring state; do not capture identities/content.

Focused regressions cover a real never-shown HWND's zero-size behavior, content
measurement, preparation followed by strict authorization, failure after Show with
no activation, and candidate-membership decisions (exact unregistered HRESULT only;
known wrong/unknown desktop, already-visible candidate and other errors fail closed).
Existing tests still cover committed effects/mailbox, freshness/generation, accounting,
legacy grace, extensions, migration and failure/retry. Automated checks are separate
from the bounded live diagnostic and the pending owner retest.

The trace also identified a publication race: the owner could offer a committed
event before publishing its corresponding exhausted snapshot. Interactive mailbox
consumption now waits for a matching epoch/generation/effect sequence rather than
dropping it against older UI state. This does not renew expiry, retry a show, or
wait for a later eligible context; ordinary eligibility and original freshness
remain binding. Legacy notification consumption is unchanged. Deterministic tests
cover publication ordering and expiry; this race is not claimed as the measured
cause of the owner's failed smoke.

The app-test executable has two explicitly invoked local diagnostics:
`--inspect-hidden-nudge` never shows/activates; `--inspect-nudge-registration`
performs the checked nonactivating Show and strict post-Show check, then closes.
Both require an already-approved exact build/x64, print only dimensions/categories
and stability booleans, start only their owned read-only worker, and never open
accounting/settings. They are not run in CI or by normal app startup.

## Second October 16 correction: PerMonitorV2 placement and shared timing (historical)

The owner retest reached the presenter but failed `Place`. Running the cold
owned-window diagnostic with the **production PerMonitorV2 manifest** reproduced
`FinalForegroundValidation` rejection on **26200.9711/x64**, SDK **10.0.401**.
Tracing showed the foreground became the hidden candidate during the initial
monitor-positioning call. The outer call already supplied SWP_NOACTIVATE. WPF's
[DPI handler](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndTarget.cs)
issues a nested SetWindowPos without that flag when the HWND changes DPI.
The earlier test executable lacked the production manifest and masked this path.
The owner's single generic status alone cannot establish which internal stage
failed on their process; this is the reproduced placement defect, pending owner retest.

Placement now applies documented WS_EX_NOACTIVATE only around positioning of the
owned HWND, verifies that it applied, and restores/verifies the exact original
extended style in finally before Show, strict authorization or explicit focus.
It suppresses implicit activation rather than forcing focus. No permanent style
change affects keyboard/accessibility or taskbar behavior. Apply/restore failure
suppresses the event. The original foreground check remains binding. Creating
the HWND at target coordinates was also tried locally but still activated during
WPF initialization; that approach is not part of the correction.

The corrected bounded registration diagnostic, now with the production manifest
and no layout warmup, reported: `Placed`, DPI changed during placement,
foreground-monitor match true, target window DPI **144 (150%)**,
candidate-is-foreground false, foreground/desktop
stable true, preparation Allowed and strict post-Show membership Allowed. No
activation was requested; the owned window closed afterward. The owner app was
not stopped; no user accounting/settings were opened. These before/after checks
are bounded evidence, not continuous shell monitoring or full owner acceptance.

Home's process-local status now identifies initial foreground validation,
monitor/work area, nonactivating initial position, DPI, minimum width, native frame,
content measurement, centering, final foreground validation and final positioning.
Guard-application/restoration failures have separate categories. No foreground
application/window/desktop identities, history, telemetry or permanent log are added.

The owner explicitly superseded legacy notification timing preservation: both
styles now use immediate durably accepted exhaustion and one shared 1–60 whole
minute repeat setting, default one minute. Settings calls it **Reminder repeat
interval** and explains both styles. The old v3 field name/shape remains readable;
v1/v2/v3, pending commits, usage, break progress, Personal-only extensions and
the legacy low-level Snooze clock remain intact. Duration edits preserve the
shared cadence; a repeat reduction caps its remainder without replaying effects.
Windows notification remains nonfocusing, single outstanding and lower priority
than retained errors. Native notification rendering/branding stays in issue #17.

Focused deterministic coverage includes guard application/restoration on success,
failure and exception; discrete presenter failure without Show/focus; both styles'
immediate committed event/shared cadence; countdown freezes; exhausted style
transitions; extension preservation across style changes; duration/cadence edits;
and restoration of Grace/Nudging from all three checkpoint versions without
Settings-time or historical effects. Existing strict desktop, mailbox, freshness,
migration and single-window tests remain. Automated and owner results are separate.

Final local validation: all seven repository-prescribed commands **PASS**, plus
final affected formatting checks after the last cadence/label regression edits.
Application results: **82 Core, 83 Persistence, 84 host, 37 WPF groups**, warnings
as errors, SDK 10.0.401. The bounded production-manifest live diagnostic passed
separately; it is never part of CI. Full owner-flow focus and extension acceptance,
fullscreen and Narrator remain **NOT RUN / owner retest pending**. No build gate,
switch/move behavior, browser attribution or notification rendering scope changed.

## Third October 16 correction: mixed-DPI production lifecycle

Owner evidence at `56f23ce`: primary 3440x1440/100% PASS; secondary 4K portrait/150%
flashed and disappeared. The old standalone worker diagnostic did not exercise
the actual observer/host/presenter interaction, so its PASS was insufficient.

The owner moved the active Codex window to the secondary for a bounded live
exercise on **26200.9711/x64**, SDK **10.0.401**. Actual work area: **2160x3768**.
The production presenter, serialized controller, real native loop/sampler and
read-only desktop worker ran against disposable settings/SQLite, not user stores.

Before correction: the event had **953 ms** remaining at consumption; placement
took **86 ms**, Show **67 ms**. Both stage-boundary foreground checks saw the
original window. The real callback loop nevertheless delivered a **foreground
event for the owned candidate**. Strict second authorization rejected exactly
`ObservationGenerationChanged:Foreground` (diagnostic original/candidate/external
mask **2**, i.e. candidate only), before any new desktop query. Accounting moved
to Waiting for evidence. This identifies self-transition generation rejection;
TTL expiry, an external foreground event and wrong desktop were not the observed
cause. The precise WPF message generating the callback was not traced; the proven
rejection is the accounting-generation check. The old guard covered synchronous
positioning only, before Show/deferred layout.

A subsequent verification also caught `ObservationGenerationChanged:Destroyed`
before initial authorization, after WPF construction. Ownership of that destroyed
HWND was not identified. The final transaction therefore begins before creating
the nudge, rather than adding another exception to original-event validation.

The correction treats acceptance/presentation/confirmation as one transaction:

1. Validate the original committed event and foreground/Personal context once,
   before constructing or initializing a nudge HWND.
2. Issue a nonrenewable two-second process-local transaction. It authorizes
   presentation, not focus or extra time; the original event is consumed.
3. Hold the nonactivation guard through positioning, nonactivating Show and WPF's
   queued DPI/layout work. Restore the exact style before completion/focus.
4. Bind the exact UI-owned HWND and complete once using a new safety capture, active Personal session/input, matching
   shell/health, original-or-owned foreground and strict current-desktop membership.
5. Verify the published transaction/context and actual foreground, then request focus once.

Before binding, at most one distinct provisional foreground HWND is held for that
transaction only and is NOT trusted. It must equal the actual candidate at
completion; any other/second foreground is rejected, including leave-and-return.
Metadata is cleared on expiry/revocation. Original/confirmed-owned foreground
events are tolerated only within that transaction. Any
external/unknown foreground (including leave-and-return), relevant destruction,
session/desktop/power change, sequence loss, health change or command revokes it.
The transaction never renews and cannot be replayed or forged by changing its
fields. The existing read-only worker and build gates are unchanged. No signal is
ignored by the estimator, no Core interval is invented, and Core/persistence/
shared reminder timing/extension/notification behavior are unchanged.

Final corrected run on the same secondary setup: **57 ms** initial acceptance,
**236 ms** unified Show/layout, **61 ms** fresh completion, actual window DPI
**144 (150%)**, foreground-monitor match true, original foreground at the final boundary, positive
strict membership, nudge remained visible and reached the activation boundary.
**Focus was deliberately not requested.** Host accounting was still Waiting for
evidence, demonstrating separation of fresh presentation safety from estimator
continuity. This is real mixed-DPI presentation/authorization evidence, not focus,
Add 1 minute, Narrator/fullscreen or daily reliability acceptance. Primary had a
real-host baseline PASS before correction and remains covered by deterministic
single-window/foreground/geometry regressions; no broad owner campaign is required.

The explicit replacement diagnostic is:

```powershell
# Run from the repository after building this commit's app tests (./run.ps1 test).
# Keep an application active on the named monitor. No nudge focus is requested.
$env:DOTNET_ROOT = (Resolve-Path .tools/dotnet).Path
.tools/dotnet/dotnet.exe run --project tests/OfframpShift.App.Tests -c Release --no-build -- --inspect-nudge-lifecycle secondary
```

It lasts at most 15 seconds waiting for one committed event, uses the production
PerMonitorV2 manifest and this checkout's UI resources, and closes its owned nudge
and controller afterward. Scratch stores are removed after verified owned-path
cleanup. Normal tests/CI never run it. It prints fixed categories, stage durations,
geometry and booleans; no HWNDs, desktop GUIDs, app keys, titles or content. The old
standalone diagnostic flags now direct users here. The initial harness attempt
accidentally pumped normal Shift startup before exiting; the corrected harness
uses a plain resource-only Application and cannot enter Shift startup. The owner
app was not stopped. This distinction applies to the recorded bounded runs above.

Focused regressions cover the observed owned-foreground/accounting-generation
transition with both original/candidate current foreground; genuine external
leave-and-return; fresh Work/lock/unknown evidence without callbacks; session,
desktop, power, relevant destruction, dropped signals, expiry, commands, forged/
replayed transactions and failed strict membership. WPF tests cover asynchronous
guard lifetime/restoration, portrait/negative-origin pixel geometry, same-window
reuse, scoped foreground acceptance and stage diagnostics. Real focus and extension
acceptance remain pending the five-step owner retest below.

Final application regressions: **82 Core, 83 Persistence, 110 host, 42 WPF groups**
passed with warnings as errors and SDK 10.0.401. All seven repository-prescribed
test/format commands **PASS**. Repository check results and
current-head CI are recorded in the PR; simulated checks and the explicit live
nonactivating exercise remain separate.

Final focus-grant review also covers health/shell changes after successful strict
confirmation: the published transaction ID becomes unavailable even if ongoing
Personal visibility remains healthy. Final affected tests/formatting were rerun
after that projection check; the UI captures each rejection reason once.

Deferred Home timer-first requirements are captured in
[the follow-up brief](../decisions/home-timer-first-follow-up.md), recommended for
a separate subsequent GitHub issue; PR 9 does not change Home's layout.

### Short owner retest (pending)

Exit the currently running app through the tray and launch the updated build;
record tested commit/build, then:

1. Exit the running Shift instance and launch the updated PR 9 build.
2. On the secondary portrait monitor at 150%, exhaust a one-minute Personal allowance in **Interactive window** mode.
3. Confirm the nudge remains visible and takes focus.
4. Confirm presentation does not change virtual desktops. Any such switch is FAIL.
5. Click **Add 1 minute** and confirm the extension works through counted Personal use.

Restore temporary duration settings afterward. If unsuccessful, report the single
Home Last reminder line plus monitoring state. Do not widen compatibility or bypass
checks; PR 10 and issue #17 remain excluded.
