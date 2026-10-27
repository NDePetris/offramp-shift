# Issue #22 - interactive reminder visibility

## Scope and evidence

Base: `origin/main` at `7a97ea4`. Issue #22 reports a reminder partly covered by
Chrome/ChatGPT. Existing positioning preserves z-order; foreground activation is
best effort. No trace proves whether the report was initial stacking, foreground
refusal or later re-occlusion. PR 9's mixed-DPI foreground-transition evidence
justifies retaining its nonactivation guard, not claiming DPI caused issue #22.

The correction adds one nonactivating `HWND_TOP` raise after strict completion and
final UI authorization. It rechecks the exact HWND, transaction, expiry, context
and actual foreground before the existing single foreground request, without a
dispatcher yield. Raise failure alone leaves an eligible reminder available;
authorization rejection closes automatically without granting extra time. The
original DPI/layout guard, placement, single-use transaction, deferrals and explicit
switching remain intact. No topmost status, activation retry or polling is added.

The passive tray hint points to the explicitly enabled taskbar button and has no
activation callback. Refresh can update/remove the hint but cannot show, raise or
activate. Close events clear the hint immediately; refresh rechecks eligibility.
Shutdown closes the reminder and disposes the tray.

Only the latest in-memory presentation result is retained: Show/layout,
authorization, raise, foreground-request return value and immediately observed
foreground role. After-Show/after-request observations contain native
visible/minimized/topmost booleans, original-monitor match and DPI. There are no
HWNDs, process identities, titles, URLs, GUIDs or activity histories in diagnostic
output/persistence. Diagnostic exceptions report Unknown without affecting
authorization/accounting. No deactivation tracking is added.

Successful native calls and native visibility are **not proof of unobscured pixels**.
Other applications can cover the reminder later. Topmost/fullscreen windows and
Windows foreground restrictions remain limitations. #17, #26 and monitoring
reliability are separate. Existing build/consent gates, automatic-switching NO-GO
and PR 5 Tests 8-12 deferrals remain unchanged.

## Automated validation

October 27, 2026, pinned SDK **10.0.401**, Windows **26300.9711/x64**:

| Required command | Result |
|---|---|
| `./run.ps1 test` | PASS - 82 Core, 84 Persistence, 316 host, 151 WPF groups at that run |
| `./run.ps1 format` | PASS |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS - 58 fake-provider cases and owned non-COM worker checks |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS - 2,053 policy/protocol assertions, 5 mocked extension tests, synthetic transport |
| `./spikes/WindowsObservation/run.ps1 test` | PASS - 41 groups, synthetic observations/local SQLite |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS - 16 synthetic/owned-process groups, no COM |
| `./spikes/WindowsObservation/run.ps1 format` | PASS |

After final diagnostic and pending-confirmation hint refinements, the affected
application/test projects were rebuilt with warnings as errors and the final WPF
suite **PASS - 153 groups, 0 failed**. Affected-project formatting was rechecked.
Tests cover strict ordering, one raise/request per event, duplicate/refresh/
deactivation suppression, expired/revoked authorization, closure, wrong desktop,
external foreground and context loss at completion/during raise, native raise
failure/exception, focus refusal, diagnostic failure and passive hint lifecycle.
Existing DPI, keyboard/accessibility, dismissal, deferral and switching checks pass.

Tests use synthetic host/platform observations, disposable persistence and owned
HWNDs. The native hidden-HWND raise check ran on the build above and verifies
nonactivation, no geometry change and no topmost state. This automated native
check does not prove real visible-window stacking or Chrome/ChatGPT behavior.
The existing opt-in lifecycle diagnostic deliberately skips raising and focus;
its PASS cannot establish this change's live stacking/focus acceptance.

All seven commands used ignored isolated outputs via `UseArtifactsOutput=true`
and `ArtifactsPath=<repository>/artifacts/issue22`, outside the tool sandbox.
The initial sandbox run encountered NuGet vulnerability-feed access failure; the
first ordinary-output run encountered locks from the owner's running app.
Isolated-output validation resolved these environmental failures without disabling
warnings/auditing, changing dependencies, stopping the owner app or editing active
application data. No live production reminder or desktop-switch exercise was run.

## Owner smoke checklist - NOT RUN for this change

Record commit, exact supported Windows build/x64, monitor layout/resolution/scaling
and whether Chrome/ChatGPT is fullscreen, elevated or topmost. Use the existing
3440x1440 landscape/100% primary and 4K portrait/150% secondary if still current;
record actual configuration rather than assuming it. Preserve original settings
before any temporary allowance adjustment. Do not edit data under the running app.

1. With Interactive window explicitly enabled, use ordinary Chrome and ChatGPT
   activity through a few eligible reminders. Include each monitor and the mixed-DPI
   transition. Record full/partial visibility separately from focus. Inspect
   Settings > Advanced > Last reminder afterward; compare raise outcome, native
   foreground return value and immediately observed foreground role.
2. Deliberately focus Chrome/ChatGPT after a reminder appears. Confirm no raise or
   focus theft from refresh. Recover through its taskbar button. Check the passive
   tray hint appears only while the eligible reminder is actually open.
3. Briefly check Tab/access keys, Add 1 minute, Add 5 minutes and dismissal. Confirm
   one durable action and prompt hint removal. Explicit Switch to Work remains
   separately gated; do not enable/exercise switching merely to check visibility.
4. Briefly check Pause or Work suppression and restore temporary settings. Any
   observed presentation-caused virtual-desktop switch is a blocking FAIL.

Record PASS/FAIL/NOT RUN and the sequence if partially covered. Foreground return
value alone cannot establish cause. No extended accounting, fullscreen, Narrator,
recovery or unattended-use campaign is required by this checklist.

## Actual owner/Windows results

NOT RUN / no new owner report. PR 9 evidence remains historical, not acceptance of
this correction. Visible-window stacking, focus refusal and dual-monitor
Chrome/ChatGPT behavior require the bounded owner check above.
