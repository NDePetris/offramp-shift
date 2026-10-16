# PR 9 — interactive nudges

Owner evaluation only. Live focus, virtual-desktop placement, mixed DPI,
fullscreen behavior and Narrator acceptance are **NOT RUN** by the agent.
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

Documented read-only `IVirtualDesktopManager` membership/desktop-ID checks run
through the existing isolated worker. Current Personal GUID, foreground, session
and shell generation are corroborated around the query. The public declaration
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
