# PR 9 — interactive nudges

Owner evaluation only. Initial owner smoke **FAIL**: Interactive window never
appeared; legacy notifications still worked, including their expected grace.
The October 16 correction and bounded registration evidence are recorded below.
Full owner-flow focus, mixed DPI, fullscreen and Narrator acceptance remain **NOT RUN** by the agent.
Automated tests use synthetic evidence, disposable SQLite, owned hidden HWNDs
and fake placement/focus adapters. They do not establish live shell reliability.

## Timing and persistence

Windows settings v3 replaces the Boolean preference with Off, Windows notification
and Interactive window. v1 migrates to Off; v2 enabled/disabled migrates to Windows
notification/Off. Original shapes are validated strictly; load never writes or
enables interactive focus. The next explicit atomic save writes v3. Existing
notification grace/repeat values remain unchanged (ordinary defaults 1/10 minutes).

Interactive mode prompts at the durably accepted exhaustion boundary, without
automatic grace, subject to existing sampling, holdback and freshness limits.
Add 1 minute, Add 5 minutes, X, Escape and Alt+F4 use a distinct durable deferral.
Extensions and repeats advance only through counted Personal usage. Work, lock,
idle, neutral, Unknown, Pause and downtime consume none; a qualifying break
clears the intervention normally. Usage continues; extra time grants no allowance.
Legacy `Snooze()` retains its all-verified-time clock.

Core stores a separate interactive repeat interval: default 60,000 ms, restricted
to 1–60 whole minutes. Its dedicated edit preserves unfinished break progress,
usage and Pause. Style and cadence have separate saves in their existing stores;
no cross-file transaction is implied.

Checkpoint v3 adds only `InteractiveRepeatNudgeMs`. The appended `PersonalDeferred`
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
Commands and changed evidence invalidate authorization; the Dispatcher rechecks
generation, foreground and expiry after host authorization. Runtime epochs prevent
recovered effect sequences colliding with earlier presentation suppression.

Continued visibility separately requires healthy active Personal context, so the
nudge taking focus does not dismiss itself. Its identity is classified normally:
CountByDefault counts it unless excluded; BlocklistOnly counts it only if included.
It becomes idle normally. No synthetic activity or UI countdown advances Core.
Pause, Work/Other, lock, unavailable/stale evidence, reset, style change, storage
failure, owner stop and exit close the nudge without granting extra time.

Documented read-only `IVirtualDesktopManager` checks run through the existing
isolated worker in two stages. Before a new window's nonactivating first Show,
current Personal GUID, foreground GUID, positive current-desktop membership,
session and shell generation must agree. A hidden new HWND may return exactly
TYPE_E_ELEMENTNOTFOUND for its own desktop ID; this permits only that first
nonactivating Show, never focus. After Show, its desktop ID must positively match
Personal under the original event/expiry/generation before activation. Already-open
windows always use strict authorization. Other failures and known wrong desktops
are rejected. There is no registration retry or focus retry. The public declaration
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

1. Confirm an existing enabled v2 reminder preference becomes Windows notification
   and retains its grace/repeat behavior. Do not edit settings under a running app.
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

## October 16 correction: missing nudge

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

### Short owner retest (pending)

Record tested commit/build, then:

1. Enable **Interactive window** and save.
2. Use a one-minute Personal allowance.
3. Confirm a nudge appears promptly after exhaustion on the active monitor.
4. Confirm presentation does not change virtual desktops. Any such switch is FAIL.
5. Confirm **Add 1 minute** defers the next prompt through counted Personal use.

Restore temporary duration settings afterward. If unsuccessful, report the single
Home Last reminder line plus monitoring state. Do not widen compatibility or bypass
checks; PR 10 and issue #17 remain excluded.
