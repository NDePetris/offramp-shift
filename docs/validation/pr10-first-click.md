# PR 10 — first-click interaction correction

## Baseline and investigation

Reviewed branch `codex/pr10-explicit-switch-to-work` at
`83ab7a6b1032ac8245513764fd737a2e70af43e6`, with a clean working tree including
untracked files. That commit already contains the intentional 26300-family policy.
This correction preserves that policy, separate consent, and automatic switching NO-GO.

The owner reports integrated Switch to Work succeeds on 26300.9711/x64, but an
initial click appears only to activate the nudge; a second click switches. There
was no mouse-message/command trace of that session, so a swallowed native click
versus a submitted/rejected request cannot be established retrospectively.

Verified code path: `ShowActivated=False` suppresses show-time activation.
`NudgePlatform` temporarily applies WS_EX_NOACTIVATE through positioning, Show
and deferred DPI layout, restores the exact original style, and only then allows
strict host desktop confirmation and one SetForegroundWindow request. Windows
may refuse that request; neither focus refusal nor activation itself switches.
There is no application mouse-activation hook or intercepted first button click.
Normal WPF Button.Click reaches SubmitSwitch, which reserves the shared submission
guard before its first await. Progress and every unsuccessful result remain visible
locally (Home/tray if the nudge closes).

A reproducible authorization race exists independently of mouse delivery: the
first Button.Click can enqueue AuthorizeSwitch before the monitoring owner samples
the foreground transition. The old authorization drained events but compared the
previously published foreground HWND, rejecting a legitimate first request. The
next sample makes a later request succeed. A deterministic regression models this
order without native observation or switching.

Correction: only when the authorized candidate differs from the sampled foreground,
take one fresh read through the existing isolated observation provider. Validate
the original eligibility/shell/health first, drain events again after the capture,
then apply every existing authorization check. External leave-and-return, lost
events, invalid context and failed activation still reject. No estimator reset,
Core operation, authorization retry, lease renewal or native switch is added.
The desktop worker's COM reply wait has a 2,000 ms deadline; failure marks the
worker unavailable and invokes existing shutdown with a 1,000 ms exit wait, with
no retry. HealthyPersonal rejects a collection taking over 500 ms, and the host
also rejects evidence over 2,000 ms old. These are reply/cleanup deadlines and
acceptance limits, not an absolute wall-clock deadline on the complete Read call:
synchronous pipe writes and local Windows session/process/resource queries remain
outside the reply timeout. The Dispatcher stays asynchronous, but a stalled local
OS query can delay the monitoring owner. This existing provider limitation is not
a claim of hard real-time cancellation or newly certified monitoring reliability.

The requested HWND must equal the strictly confirmed switchWindow both before
and after capture, and the captured foreground must equal that exact HWND.
Sampler brackets foreground/session reads and marks changed foreground unavailable;
event draining independently revokes external transitions, including leave-and-return.
The switch worker checks foreground again before its native call. A capture cannot
substitute an unrelated foreground window for the confirmed nudge. User/shell changes
after the final check remain the existing non-atomic race, not permission to bypass it.

An owned, never-shown WPF HWND test queries WM_MOUSEACTIVATE without sending a
mouse click or requesting foreground: MA_ACTIVATE means Windows retains the
original mouse message. A separate routed Button.Click test starts inactive with
simulated focus refusal, submits exactly once, suppresses duplicates and ignores
late completion after closure. These do not reproduce an actual pointer gesture
or prove the owner's native first-click behavior. No speculative Win32 hook,
replayed input, topmost mode or focus workaround is added.

References: [WM_MOUSEACTIVATE return semantics](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mouseactivate),
[WPF Window source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs).

## Owner acceptance — not run by the agent

Use the [integrated setup and consent procedure](pr10-integrated-26300.md).
Exit an older instance through its tray. In an ordinary unelevated x64 PowerShell
session, review the exact committed revision before building. The commit must
contain this correction, rather than baseline `83ab7a6` alone:

```powershell
Set-Location G:\Projects\offramp-shift
git branch --show-current
git rev-parse HEAD
git status --short --untracked-files=all
git diff --check
git show --stat HEAD
./run.ps1 build
if ($LASTEXITCODE -ne 0) { throw 'Stop: build failed' }
$appDir = (Resolve-Path src/OfframpShift.App/bin/Release/net10.0-windows).Path
Get-FileHash -LiteralPath @((Join-Path $appDir 'OfframpShift.App.dll'), (Join-Path $appDir 'OfframpShift.ApplicationHost.dll')) -Algorithm SHA256
```

The following owner-run launch activates real observation/monitoring intent; run
only for the accepted integrated exercise, never as an automated test:

```powershell
& (Join-Path $appDir 'OfframpShift.App.exe')
```

1. Verify Personal/Work roles independently in Task View. Use a normal unpinned
   Personal application as foreground. Keep separate switching consent off at
   first: the switch action must be disabled, with the consent reason and manual
   Win+Tab fallback. Enable and save switching consent only for the deliberate test.
2. Reach a new nudge by normal counted Personal usage and exhaustion/repeat
   behavior. No Show nudge now command exists; do not reset usage, edit storage or
   shorten thresholds. Keep the other application focused until the nudge appears.
   Record whether the nudge acquired focus; do not click its caption to prepare it.
3. Click Switch to Work **once** directly, including if the nudge is inactive.
   Require progress followed by one confirmed result and closure. Do not click
   again to compensate. Independently inspect Task View for Work, then return to
   Personal manually. If no progress appears, retain that distinction from a
   visible rejection; report the retained Home/tray result without raw identities.
4. On subsequent natural nudges check Add 1 minute, Add 5 minutes, X, Escape and
   Alt+F4 retain their existing durable deferral behavior. Confirm Tab/focus cues,
   access keys/Space, and Narrator name/progress/result announcements; merely
   focusing a control or pressing Enter on the heading must not switch.
5. Check placement, readable controls, scrolling and keyboard access with the
   foreground application on the primary **3440×1440/100%** and secondary portrait
   **2160×3840/150%** monitor. This is real mixed-DPI acceptance, not established by
   geometry tests. Further native switches need individual owner approval; one
   successful attempt does not require repeating native switching on each monitor.
6. Disable and save switching consent afterward; verify unavailable on a later
   nudge and manual Win+Tab fallback. Stop for wrong destination, instability,
   unexpected window movement, uncertainty, absent feedback or cleanup failure.
   Pause/Exit and inspect Task View manually; never retry or compensate automatically.

Windows may deny the initial focus request. The nudge must remain clickable and
reachable through its taskbar button/keyboard; no focus bypass is promised. If a
single click still only activates with no progress, this patch is not accepted:
record it for a narrowly scoped native input investigation. Do not repeatedly click.

## Separate limitations and deferred product decision

The owner observed CandidateWrongDesktop with a foreground application configured
to **Show this window on all desktops**; a normal unpinned application resolved it.
Pinned/cross-desktop identity remains conservatively rejected. Do not accept such
windows automatically, weaken membership checks or relocate windows to pass a test.

Follow-up accounting decision, explicitly **not implemented in PR 10**: verified
time on unlocked Personal should count regardless of keyboard/mouse inactivity;
idle-on-Personal must not earn break credit. Current accounting remains unchanged,
including classification, idle/break thresholds, allowance, checkpoints and media
detection. Existing idle behavior can lengthen the wait for a natural nudge.

## Automated validation

All seven prescribed checks passed on October 19, 2026 with pinned SDK 10.0.401
and warnings as errors. The new first-click host regression failed against the
original implementation (282 host passes, one failure), then passed after the fix.

| Command | Result |
|---|---|
| `./run.ps1 test` | PASS: Core 82 / 38,755 assertions; Persistence 83 / 772; Host 283 / 2,438; WPF 80 / 242; zero failures |
| `./run.ps1 format` | PASS |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS: 58 fake-provider tests plus three non-COM process-isolation cases |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS: 2,053 assertions, five extension tests and synthetic process/pipe transport |
| `./spikes/WindowsObservation/run.ps1 test` | PASS: 41 tests / 10,282 assertions |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS: 24 unsupported-entry cases, exact/family matrices and 16 diagnostic groups; no COM |
| `./spikes/WindowsObservation/run.ps1 format` | PASS |
| `git diff --check` | PASS |

Ignored logs are under `artifacts/pr10-first-click-*.log`. Tests exercise
simulated providers, WPF routing/owned HWNDs and non-COM worker faults. Actual
pointer delivery, Windows foreground policy, both displays, Narrator and native
switch behavior require the owner exercise above; no new live PASS is claimed.

Final pre-commit review: exactly seven files cover the authorization correction,
focus-refusal guidance, targeted host/WPF regressions and these architecture/owner
instructions. No unrelated files or compatibility, consent, native switching,
Core accounting or persistence changes are included. The seven passing checks
above still apply to the unchanged implementation; final review only clarified
provider timing limits and committed-build instructions. `git diff --check` passed.
