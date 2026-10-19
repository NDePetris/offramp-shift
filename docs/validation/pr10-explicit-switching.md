# PR 10 — explicit Switch to Work

Prepared October 18, 2026. PR #18 was independently verified merged into main;
checkout base is `63c3f075148a69fd36e95553b86f5985e7859cfa`. No remote PR,
merge, release or live qualification is part of this implementation.

**Normal application switching on 26200.9711/x64 remains DISABLED.** Read-only
approval from PR #13 is insufficient. Saving separate switching consent cannot
bypass the capability gate. The historical 26200.9457/x64 limited explicit
experiment gate remains exact, separately consented, and is not certification
for automatic switching or unattended use.

## Automated validation

The root application tests include fresh/revoked/expired/single-use authorization,
exact-build gates, bindings/session/foreground/topology/shell changes, one-call
native policy, typed completion validation, worker hang/crash/malformed/oversized
output, controller overlap exclusion, independent owner progress and graceful
shutdown, WPF closure/results, duplicate submissions, uncertainty lockout, strict
v1–v4 settings and accounting/estimator non-mutation. Stores are disposable SQLite;
native sessions are fakes and fault children never construct a native session.

All seven prescribed commands must pass on the final implementation:

```powershell
./run.ps1 test
./run.ps1 format
./spikes/VirtualDesktop/run.ps1 test
./spikes/BrowserAttribution/run.ps1 test
./spikes/WindowsObservation/run.ps1 test
./spikes/WindowsObservation/compatibility.ps1 test
./spikes/WindowsObservation/run.ps1 format
```

Final local automated results (SDK 10.0.401, warnings as errors):

| Check | Result and evidence boundary |
|---|---|
| `./run.ps1 test` | PASS after targeted corrections: 82 Core, 83 Persistence, 191 host and 78 WPF groups. Core/host providers and presentation are synthetic; SQLite and owned non-COM process tests are real. Host: 1,611 assertions; WPF: 233 assertions. |
| `./run.ps1 format` | PASS, including the extracted library; new test initializer whitespace was corrected before the final pass. |
| VirtualDesktop test | PASS: original 58 fake-policy cases (including 1,000 topology permutations) and three owned non-COM isolation checks. |
| BrowserAttribution test | PASS: five extension/manifest tests and synthetic native transport/process tests; no browser matrix. |
| WindowsObservation test | PASS: 41 tests / 10,254 assertions, synthetic evidence and local SQLite. |
| Compatibility test | PASS: 12 unsupported-build/architecture entry cases, presentation decisions, 14 diagnostic groups and owned non-COM worker faults; no COM. |
| WindowsObservation format | PASS. |
| Focused final checks | PASS: post-grant invalidation, closure/shutdown revocation, unexpected destination lockout, unavailable primary action and actual worker entry guards with fake activation factory. Off-screen light/dark nudge renders inspected; no focus/presentation qualification inferred. `git diff --check` passes. |

The watchdog test holds the switch worker's stdin open and empty: authorization
cannot be decoded and native activation cannot be reached. Its independent timer
exits that owned STA child with code 124. This is a non-COM lifetime check, not
evidence about shell behavior or an unkillable native process.

An ignored `artifacts/pr10-build`
output is used locally via `UseArtifactsOutput=true` and absolute `ArtifactsPath`
for root app checks because the owner's running app locks the ordinary output.
No owner process is stopped or restarted. Initial restricted restores could not
access NuGet audit metadata; permitted network-enabled restore retained pinned
package versions and warnings as errors. Probe checks use their ordinary output.

**Manual Windows switching PASS: none for PR 10.** No live switch, observation
diagnostic, Explorer restart, desktop manipulation or OS experiment was performed.
Off-screen WPF render inspection is layout evidence only. Actual transitions,
Narrator, full keyboard interaction, high contrast, mixed DPI, fullscreen and
shell races remain untested for this change. PR 5 Tests 8–12 remain deferred;
PR 9's owner-accepted timing uncertainty remains unresolved.

## Targeted safety corrections and re-review

The pre-qualification conditions are corrected without changing capability gates,
COM declarations, Core accounting or checkpoint formats. Runtime invalidation now
revokes the issued grant itself. Its existing atomic exchange serializes revocation
and consumption across the monitoring owner and executor; expiration and single-use
checks remain intact. Foreground return cannot restore a terminal grant. Window
closure and shutdown revoke outstanding grants, without canceling a consumed
request or reversing a shell side effect. Monitoring does not wait for native work.

`WrongDestinationOrUserRace` now maps to Indeterminate. The diagnostic category and
native result remain intact; the UI locks out further attempts and instructs Task
View inspection. Cached host availability disables known consent/build-ineligible
actions before progress begins, with independent host authorization on real requests.

New regression groups: 11 post-grant runtime/command invalidation cases, availability
projection, two controller revocation cases, 19 worker entry guard groups, two WPF
availability cases, unexpected-destination UI lockout and command-line entry guards.
Existing outcome/closure tests additionally check closure cancellation. Worker guard
positive controls reach only an injected throwing factory, never native COM.

Exact files changed for these corrections (the broader pre-existing PR 10 diff
remains uncommitted):

| Purpose | Files |
|---|---|
| Host revocation and availability | `src/OfframpShift.ApplicationHost/MonitoringRuntime.cs`, `MonitoringApplication.cs`, `MonitoringController.cs`, `HostSnapshot.cs` |
| Native result classification and test seam | `src/OfframpShift.Switching/Switching.cs`, `SwitchWorkerEntry.cs`, `OfframpShift.Switching.csproj` |
| WPF availability and lifetime wiring | `src/OfframpShift.App/NudgeWindow.xaml`, `NudgeWindow.xaml.cs`, `NudgePresenter.cs`, `App.xaml.cs` |
| Host regressions | `tests/OfframpShift.ApplicationHost.Tests/SwitchRegressions.cs`, `SwitchPolicyRegressions.cs`, new `SwitchWorkerGuardRegressions.cs`, `Program.cs` |
| UI/entry regressions | `tests/OfframpShift.App.Tests/InteractiveUiRegressions.cs`, `Program.cs` |
| Documentation | `docs/architecture/explicit-switching.md`, `docs/validation/pr10-explicit-switching.md` |

Correction logs are under ignored `artifacts/pr10-corrections-*.log`. Re-review:
**PASS for these targeted pre-qualification corrections.** Bounded diagnostic
qualification still requires separate owner authorization. Integrated normal
switching on 26200.9711/x64 remains blocked pending its own capability decision.
Automatic switching remains NO-GO. Native TOCTOU races and delayed shell effects
remain inherent uncertainties; synthetic tests do not establish live compatibility.

## Owner qualification: diagnostic authorization first

Do not run this procedure until the owner separately authorizes actual-Windows
switching qualification. The diagnostic command is deliberately isolated from
normal startup, WPF, singleton, user settings writes and accounting. It accepts
only exact 26200.9711/x64. It cannot enable application capability, alter any
allowlist, retry, switch back, move windows or send keyboard input.

1. Save work and use an ordinary unelevated Windows PowerShell session. Record
   tested commit (and dirty diff if applicable), SDK, actual Windows build/UBR,
   OS/process architecture, privileges/session, display count/resolutions/layout/
   scaling and shell configuration. Independently confirm 26200.9711/x64; do not
   copy a historical host description. Exit Shift yourself through its tray.
2. Run all automated checks above. Preserve local settings/checkpoints while
   stopped. Read the existing distinct Personal/Work GUID assignments locally;
   independently check each role in Task View using the already approved
   read-only setup. Do not use names/indices to select a replacement GUID.
   Missing/deleted assignments require deliberate reconfiguration, not substitution.
3. With Personal selected manually and PowerShell foreground, run exactly ONE
   diagnostic invocation, substituting the locally checked GUIDs:

   ```powershell
   $env:DOTNET_ROOT = (Resolve-Path .tools/dotnet).Path
   # Default output after ./run.ps1 test, with the app stopped during the build:
   $appDll = (Resolve-Path src/OfframpShift.App/bin/Release/net10.0-windows/OfframpShift.App.dll).Path
   .tools/dotnet/dotnet.exe $appDll --qualify-switch-26200.9711 --confirm-experimental '<Personal GUID>' '<Work GUID>'
   ```

   If using the isolated artifacts output described above, instead resolve
   `artifacts/pr10-build/bin/OfframpShift.App/release/OfframpShift.App.dll`.
   Never invoke either private worker mode directly. The command first takes a
   fresh isolated snapshot, then supervises a second single-request switching
   worker. It prints a typed category and manual fallback; it does not print
   GUIDs, names, titles, content or browsing history.
4. Independently inspect Win+Tab after every result. Record the outcome, elapsed
   time, whether Work was actually reached, any transient wrong destination,
   focus/minimization/window effects and whether the helper exited. Verified API
   identity alone is insufficient visual corroboration. An uncertain result may
   include a delayed transition: stop, inspect manually, and do not retry to
   compensate. Return to Personal manually if needed; there is no return leg.
5. Stop on an unexpected destination, shell instability, an uncertain result,
   a worker remaining alive beyond its bounded cleanup, or any window movement.
   Retain only a sanitized local result summary with precise environment/provenance;
   do not commit GUIDs or user content. Do not restart Explorer as a recovery test.

## Bounded qualification matrix and separate capability decision

Mark each row PASS, FAIL, BLOCKED or NOT RUN, with actual build/hardware and tested
revision. Each additional native attempt requires a fresh deliberate operator
decision after the prior result and manual context check; no loops or scripts.

| Owner case | Expected evidence |
|---|---|
| One explicit Personal to Work attempt | At most one call; correct API identity plus independent Task View corroboration; no unrelated window movement |
| Source is already Work or another desktop | Rejected with zero attempted switches; no compensating transition |
| Missing/deleted destination GUID or equal roles | Rejected; no index/name replacement |
| Changed foreground/source during startup | Rejected if seen before call; after-call race reported without retry and manually inspected |
| Primary landscape / secondary portrait at actual scaling | Correct destination and stable windows; record each display separately |
| Pinned foreground window/app, shell foreground | Independent current GUID remains authoritative; ambiguity must reject safely |
| Session/input unavailable | No attempt unless freshly interactive; do not automate lock/sleep or expand this into the deferred accounting campaign |
| Unknown build/architecture and ordinary app on 9711 | Normal application capability remains blocked even with saved consent |

Potentially disruptive deletion, pinned-window and session stimuli are owner-run
only if separately authorized. Do not treat their NOT RUN status as a pass.
No 200-transition automatic-switch certification campaign is included here.

After reviewing actual evidence, the owner must make a **separate documented
application-capability decision** for exactly 26200.9711/x64. A diagnostic PASS
alone does not change code or settings support. Any later application gate change
needs a focused reviewed patch and explicit owner authorization. Application nudge
focus/keyboard/Narrator/high-contrast/mixed-DPI acceptance then remains a separate
check on that enabled revision. Automatic switching stays NO-GO regardless.
