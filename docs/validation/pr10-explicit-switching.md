# PR 10 — explicit Switch to Work

> Current owner decision: limited 26300.9711/x64 standalone qualification was owner-reported PASS and accepted for controlled integrated testing. The 26300 family policy is now implemented in the current patch. No further standalone repetitions are required. See [current evidence, scope and WPF test procedure](pr10-integrated-26300.md). The preparation gates/results/procedure below describe historical commit `de869b5`, not the current capability boundary.

Prepared October 18, 2026. PR #18 was independently verified merged into main;
original checkout base was `63c3f075148a69fd36e95553b86f5985e7859cfa`; the original implementation is committed as `45c0d73`. No remote PR,
merge, release or live qualification is part of this implementation.

**Historical preparation gate:** normal application switching was disabled on 26200.9711/x64 and all 26300 builds. Current 26300-family owner testing is described above; 26200.9711 remains switching-disabled. Read-only
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

Historical PR 10 automated results at implementation commit `45c0d73` (SDK 10.0.401, warnings as errors; current preparation results follow below):

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
output was used for historical PR 10 validation via `UseArtifactsOutput=true` and absolute `ArtifactsPath`
for root app checks because the owner's running app locks the ordinary output.
No owner process is stopped or restarted. Initial restricted restores could not
access NuGet audit metadata; permitted network-enabled restore retained pinned
package versions and warnings as errors. Probe checks use their ordinary output.

**Agent-performed manual Windows switching PASS: none.** The subsequent owner-reported limited qualification is recorded in the current integrated procedure. No live switch, observation
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

Historical files changed for these corrections (committed in `45c0d73`):

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

## 26H2 preparation — October 19, 2026

Preparation base: `45c0d73` on `codex/pr10-explicit-switch-to-work`. The owner
reports Windows 11 26H2, **26300.9711/x64**. No live compatibility result is
claimed. This patch adds exact-build diagnostic eligibility only; it neither
approves the ABI nor activates the proposed [build-family policy](../architecture/explicit-switching.md#active-26300-family-policy--controlled-owner-evaluation).

| Capability | Effective gate after this preparation |
|---|---|
| Normal application switching | Exactly 26200.9457/X64, existing separate consent and runtime authorization |
| Standalone switching qualification | Exactly 26200.9711/X64 or 26300.9711/X64; command must name the actual build |
| Production read-only observation | Exactly 26200.9457/X64 or 26200.9711/X64 |
| Standalone read-only compatibility diagnostic | Exactly 26200.9711/X64/25H2 or 26300.9711/X64/26H2, x64 OS and process, ordinary interactive session and explicit matching target |
| Automatic switching | NO-GO |

The standalone read-only harness privately compiles the existing ABI and gates
parent/child separately. Its 26300 preparation does **not** modify
`DesktopObservationPolicy.IsSupported`, normal snapshot/watch, monitoring or setup.
The switching diagnostic starts before WPF, singleton, settings and checkpoints.
Every invocation captures fresh evidence and supervises one single-request switch
worker; both worker/native entry gates remain exact. GUID checks, lease, one call,
STA isolation, watchdog, bounded cleanup and no retry remain in place. No ABI slot
changes were made. Existing 26200 capability and command gates are preserved.

## Owner qualification: separately authorized, minimal 26300 procedure

**Historical standalone procedure at `de869b5`:** the owner has now completed the limited Personal/Work reads and one switch and approved proceeding to the integrated WPF test. Do not require another standalone attempt or the optional repetitions below; use the current integrated procedure linked at the top.

**Historical preparation status: live compatibility was NOT RUN by the agent.** Save work and exit Shift yourself through its tray.
Keep it stopped while rebuilding and qualifying. Use ordinary unelevated **x64
Windows PowerShell**, outside the tool sandbox. Do not use private worker modes,
`run.ps1 app`, the original spike's switch/capture commands, Explorer restart,
topology changes, pinning tests or the historical large manual matrix.

Each command block below has one authorization label. **SAFE NOW — no live
switching** means no undocumented desktop COM is activated; it does not authorize
the later read-only or switching blocks. Do not use a transcript, output redirection
or literal GUID command arguments during role checks/switching. Role values stay
in local variables and private worker IPC; do not paste settings or process command
lines into evidence. No GUID belongs in committed documentation or logs.

### 1. SAFE NOW — no live switching: environment and reviewed revision

```powershell
Set-Location G:\Projects\offramp-shift
git branch --show-current
git rev-parse HEAD
git show -s --format='%h %s' HEAD
git merge-base --is-ancestor 45c0d73 HEAD
if ($LASTEXITCODE -ne 0) { throw 'Stop: original PR 10 implementation is missing' }
if (@(git status --porcelain).Count -ne 0) { throw 'Stop: working tree must be clean' }
$reviewedCommit = (git rev-parse HEAD).Trim()
$windows = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$windows | Select-Object DisplayVersion, CurrentBuild, UBR
[Environment]::Is64BitProcess
$env:PROCESSOR_ARCHITECTURE
$env:DOTNET_ROOT = (Resolve-Path .tools/dotnet).Path
.tools/dotnet/dotnet.exe --version
Get-Content global.json
```

Require branch `codex/pr10-explicit-switch-to-work`, the **exact preparation commit
SHA in the final review report**, the requested preparation commit subject and a
clean tree. Compare `$reviewedCommit` locally with that report before proceeding;
`45c0d73` alone does not include the preparation. Require 26H2, CurrentBuild 26300,
UBR 9711, Is64BitProcess true and PROCESSOR_ARCHITECTURE AMD64. Independently
corroborate Windows Settings and Display Settings count/layout/resolutions/scaling.
The no-COM report below independently checks the diagnostic's OS/process architecture,
privileges and interactive state; registry ProductName alone is insufficient.

### 2. SAFE NOW — no live switching: rebuild and no-COM checks

All seven automated commands were run during final review. Rebuild the diagnostic
from the clean reviewed checkout instead of using an older binary. `build` compiles
the app through its test project references; it does not start WPF or monitoring.
Require exit 0 after each command:

```powershell
./run.ps1 build
if ($LASTEXITCODE -ne 0) { throw 'Stop: reviewed app build failed' }
./spikes/WindowsObservation/compatibility.ps1 dry-run -TargetBuild 26300.9711
if ($LASTEXITCODE -ne 0) { throw 'Stop: no-COM environment report failed' }
$appDll = (Resolve-Path src/OfframpShift.App/bin/Release/net10.0-windows/OfframpShift.App.dll).Path
Get-FileHash -LiteralPath @($appDll, (Join-Path (Split-Path $appDll) 'OfframpShift.Switching.dll')) -Algorithm SHA256
if ((git rev-parse HEAD).Trim() -ne $reviewedCommit -or @(git status --porcelain).Count -ne 0) { throw 'Stop: reviewed source changed' }
```

Require pinned SDK 10.0.401/latest patch; dry-run registry **26300.9711/26H2**,
x64 OS **and process**, `elevated: false`, `interactive: true`,
`eligibleForOptIn: true`, **`productionSupported: false`**,
`productionEntryGuardsRejectedWithoutWorker: true`, `comActivated: false`.
`-TargetBuild` selects the live opt-in token; dry-run reports host eligibility and
never activates COM. Preserve revision, source digest and app/library hashes.
Stop if any required environment fact differs or is unavailable.

If repeating the final automated review, these are also **SAFE NOW — no live
switching**, individually requiring exit 0:

```powershell
./run.ps1 test
./run.ps1 format
./spikes/VirtualDesktop/run.ps1 test
./spikes/BrowserAttribution/run.ps1 test
./spikes/WindowsObservation/run.ps1 test
./spikes/WindowsObservation/compatibility.ps1 test
./spikes/WindowsObservation/run.ps1 format
git diff --check
```

These use synthetic providers, disposable SQLite and owned non-COM workers. They
do not qualify actual Windows shell behavior. Never reuse an old binary after
changing source or checking out a different commit.

### 3. SAFE NOW — no live switching: load existing local role identities

Read only the existing settings at the actual configured path (default below).
Do not edit consent, build or GUIDs to bypass a gate:

```powershell
$settingsPath = Join-Path $env:LOCALAPPDATA 'Offramp\Shift\windows-settings.json'
$roles = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$personal = [guid]$roles.Personal
$work = [guid]$roles.Work
if ($personal -eq [guid]::Empty -or $work -eq [guid]::Empty -or $personal -eq $work) { throw 'Stop: missing or invalid role assignments' }
$layout = Read-Host 'Actual anonymous display count, layout, resolutions and scaling/DPI'
```

Loading settings does **not** verify human role mapping. Independently identify
Personal and Work in Task View by their intended context, without creating,
renaming or reordering desktops. The next two reads compare the actual current
GUID against each saved assignment. If a role is missing, deleted/recreated,
misassigned or uncertain, stop and report BLOCKED; do not substitute names/indices,
reconfigure production setup on 26300 or proceed to a switch.

### 4. READ-ONLY OS DIAGNOSTIC — requires owner review: verify both role endpoints

Only after separately reviewing/authorizing read-only diagnostic use, independently
select **Personal** in Task View. Keep PowerShell there and run one read:

```powershell
./spikes/WindowsObservation/compatibility.ps1 once -TargetBuild 26300.9711 -ConfirmExperimentalReadOnly -OutsideToolSandbox -MonitorLayoutAndDpi $layout -ExpectedCurrent $personal.ToString()
$LASTEXITCODE
```

This is the first command that activates undocumented read-only COM. Require
PASS, exit 0, `expectedCurrentMatched: true`, one successful read, no errors/timeouts,
`ownedWorkerExitConfirmed: true`, and **`productionSupported: false`**. Retain only
the sanitized summary; the expected/observed GUIDs are never printed by the harness.

After success, independently select **Work** in Task View and open an ordinary
unelevated x64 PowerShell console **there**. Leave the original console on Personal;
do not move or pin it. Variables do not transfer between consoles. In the Work
console first run this **SAFE NOW — no live switching** preparation:

```powershell
Set-Location G:\Projects\offramp-shift
$reviewedCommit = Read-Host 'Exact reviewed preparation commit SHA from the final report'
if ((git rev-parse HEAD).Trim() -ne $reviewedCommit -or @(git status --porcelain).Count -ne 0) { throw 'Stop: reviewed source differs' }
$roles = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'Offramp\Shift\windows-settings.json') -Raw | ConvertFrom-Json
$work = [guid]$roles.Work
if ($work -eq [guid]::Empty) { throw 'Stop: missing Work assignment' }
$layout = Read-Host 'Actual anonymous display count, layout, resolutions and scaling/DPI'
```

Use the actual configured settings path if different, as in step 3. Then, still on
independently selected Work, run this **READ-ONLY OS DIAGNOSTIC — requires owner
review** as a separate operator action:

```powershell
./spikes/WindowsObservation/compatibility.ps1 once -TargetBuild 26300.9711 -ConfirmExperimentalReadOnly -OutsideToolSandbox -MonitorLayoutAndDpi $layout -ExpectedCurrent $work.ToString()
$LASTEXITCODE
```

After successful visual/identity/cleanup checks, return to Personal **manually**
and resume in the **original Personal console**, which retains `$personal`,
`$work`, `$appDll` and `$reviewedCommit`. No diagnostic moves a console window.

Require the same PASS, identity, cleanup and production-disabled results, and stable
desktop count. Any mismatch fails as `ExpectedCurrentDesktopMismatch`; stop rather
than relabeling a GUID. These are endpoint checks corroborated by the owner; they
do not certify continuous observation, accounting, future shell generations or
production compatibility. Do not change topology or restart Explorer between checks.

### 5. SAFE NOW — no live switching: final source and manual context check

After both read-only checks passed, independently select **Personal** in Task View
and return focus to PowerShell. Recheck the unchanged reviewed source:

```powershell
if ((git rev-parse HEAD).Trim() -ne $reviewedCommit -or @(git status --porcelain).Count -ne 0) { throw 'Stop: reviewed source changed' }
```

Avoid other desktop/focus changes during the following command. Read-only approval
does not authorize switching. Obtain a **separate owner authorization** for exactly
one native Personal → Work attempt before proceeding.

### 6. LIVE SWITCH — do not execute without separate authorization

Run exactly once after that separate authorization, using the variables above:

```powershell
.tools/dotnet/dotnet.exe $appDll --qualify-switch-26300.9711 --confirm-experimental $personal.ToString() $work.ToString()
$LASTEXITCODE
```

This isolated command accepts only exact 26300.9711/X64, takes a fresh isolated
snapshot, validates Personal/Work GUIDs and an unexpired request, then supervises
one single-request worker with at most one native switch attempt. It opens no
WPF application, singleton, settings or checkpoint store. It does not switch back,
move windows, inject input, retry or grant normal application capability.

### 7. SAFE NOW — no live switching: independent inspection and manual return

There is no automatic verification or return command. The Personal console may
be hidden after a successful transition. First inspect Win+Tab while on the
destination, then return to Personal manually to read its console result/exit code.
Do not bring the console back by moving or pinning it. Require `ConfirmedSuccess`, exit 0 **and
actual Work in Task View**, with no transient wrong destination, unexpected window
movement/minimization or Explorer instability. API success describes only the
bounded verification point; a delayed shell effect is still possible.

Check Task Manager Details locally: no owned qualification capture/switch worker
should remain (`OfframpShift.App`, or `dotnet` whose command line names the reviewed
DLL and qualification mode). Only the console host should remain. Do not copy
command lines, which may contain GUIDs, into evidence. Return to **Personal manually**
using Task View when safe. Do not use a switch command as a return leg.

Only if **all** success, destination and cleanup checks passed, the owner may
individually approve up to **two additional attempts**. Before each one: manually
return to Personal, verify it in Task View, restore PowerShell foreground and
recheck unchanged source using step 5. Then explicitly approve and run the single
**LIVE SWITCH** command in step 6 once. Repeat step 7 afterward. Maximum three
Personal → Work diagnostic invocations; no loops, batches, retry or automatic return.
Optional attempts may be NOT RUN; omissions are not PASS.

Stop the entire procedure on any rejected/nonzero/missing/indeterminate result,
incorrect/transient wrong destination, Explorer instability, unexpected movement
or minimization, role mismatch, or unconfirmed worker cleanup. An uncertain result
may include a delayed transition: inspect manually; do not compensate with another
command, restart qualification silently, restart Explorer or alter desktop topology.
The normal nudge's indeterminate lockout is unchanged; the standalone diagnostic
exits and relies on the operator to stop. Report any owned leftover process and
use manual recovery only; never terminate unrelated processes.

Retain sanitized PASS/FAIL/BLOCKED/NOT RUN summaries with exact OS/build/hardware,
reviewed commit, source/binary hashes, command category/exit/outcome, Task View
corroboration, display/window effects and confirmed worker exits. Do not retain
raw GUIDs, settings, process lists, titles or content. Preserve failed attempts.
Diagnostics do not write normal settings/checkpoints.

Successful smoke evidence still requires a **separate owner decision and focused
patch** before normal switching can support the 26300 family. Production observation
requires its own approval. No live result is recorded here. PR 5 Tests 8–12 remain
deferred, issues #8/#9 remain unresolved, and automatic switching and production
website attribution remain NO-GO.

## Current preparation automated results

All seven prescribed commands passed with SDK **10.0.401**, warnings as errors.
Final review reran all seven commands, including the complete regression matrix and the new expected-role comparison test.
`git diff --check` also passes. Final-review logs are local and ignored under `artifacts/pr10-review`; earlier preparation logs remain under `artifacts/pr10-26300`.

| Command | Final automated result |
|---|---|
| `./run.ps1 test` | PASS, exit 0: Core 82 tests / 38,755 assertions; Persistence 83 / 772; Host 264 / 2,141; WPF 78 / 242; zero failures. Log: `root-test.log`. |
| `./run.ps1 format` | PASS, exit 0, verify-no-changes. Log: `root-format.log`. |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS, exit 0: 58 fake-policy cases including 1,000 topology permutations; three owned non-COM isolation checks. Log: `virtual-desktop.log`. |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS, exit 0: 2,053 policy/protocol assertions, five extension/manifest tests and synthetic process/stdio/pipe tests. Log: `browser-attribution.log`. |
| `./spikes/WindowsObservation/run.ps1 test` | PASS, exit 0: 41 tests / 10,254 assertions, zero failures. Log: `windows-observation.log`. |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS, exit 0: 16 unsupported production worker-entry configurations, presentation decisions, 16 diagnostic groups including owned non-COM faults; zero failures. Log: `compatibility.log`. |
| `./spikes/WindowsObservation/run.ps1 format` | PASS, exit 0, verify-no-changes. Log: `observation-format.log`. |

The switching worker-entry matrix covers 90 mode/build/architecture combinations;
positive controls use a throwing fake activation factory, never COM. Normal
application 26300 rejection is exercised even with a valid grant; diagnostic
single-attempt success uses a fake native session. Authorization, classification,
worker isolation, WPF and accounting regressions remain covered. Core/host/native
providers are synthetic; SQLite and owned non-COM processes are real. These passes
do not establish ABI compatibility, live desktop behavior or accounting accuracy.
Initial restricted build/helper runs failed; permitted reruns passed. No owner
process was stopped by the agent. No live read, switching qualification, Explorer
restart or topology change was performed. Final review authorizes one local preparation commit only; no push, PR, merge or release is authorized.

## Final preparation review

**PASS WITH CONDITIONS for diagnostic preparation.** Reviewed the complete patch
against `45c0d73`: 18 changed tracked files and no untracked non-ignored files.
All seven prescribed checks and `git diff --check` passed. No production 26300
observation/switching predicate, ABI declaration, normal grant consumption,
worker isolation, native-attempt limit, timeout/cleanup or accounting behavior
was changed. Existing 26200 gates remain intact. No live diagnostic was run.

Corrected two practical runbook gaps: anonymous reads alone could not verify the
saved role GUIDs, and the earlier provenance instructions still allowed a dirty
base-plus-patch instead of requiring the exact reviewed commit. The existing
one-shot read now optionally compares `-ExpectedCurrent` with the actual GUID;
synthetic regressions cover a different extant/unknown/empty identity, invalid
health and shell/topology changes. No identities are printed or persisted.
The runbook uses a separate owner-opened console on Work, since the Personal console may be hidden there; it requires manual Task View corroboration for each role, a clean reviewed
commit, fresh build, explicit command categories and separate read/switch approvals.
Historical PR 10 implementation references identify its committed revision.

Conditions: live native compatibility and accessibility remain unverified; owner
read-only review and a separate one-attempt switching authorization are still
required. Wrong roles, any failed/uncertain result, instability or unconfirmed
cleanup stop qualification. Diagnostic success never enables normal capability.
The 26300 family policy is inactive, exact-build consent is unchanged and future
family consent must start off with an explicit new decision. Automatic switching
remains NO-GO. Local commit subject: `test: prepare Windows 26H2 switching qualification`.
Use its SHA from the final review report for owner-run provenance; no remote action
is authorized.

## Preparation file inventory

All paths are repository-relative. No settings schema, production observation
predicate, normal switching predicate, COM declaration or accounting file changed.

| Purpose | Changed files |
|---|---|
| Exact diagnostic switching predicate and command/host-build check | `src/OfframpShift.Switching/Switching.cs`, `src/OfframpShift.Switching/SwitchWorkerEntry.cs`, `src/OfframpShift.App/Program.cs` |
| Separate read-only diagnostic target, parent/child opt-in guards and optional expected-current comparison | `spikes/WindowsObservation/compatibility.ps1`, `spikes/WindowsObservation/Compatibility/Policy.cs`, `spikes/WindowsObservation/Compatibility/Program.cs`, `spikes/WindowsObservation/Compatibility/OwnedWorker.cs` |
| Deterministic switching and command-entry regressions | `tests/OfframpShift.ApplicationHost.Tests/SwitchPolicyRegressions.cs`, `tests/OfframpShift.ApplicationHost.Tests/SwitchWorkerGuardRegressions.cs`, `tests/OfframpShift.App.Tests/Program.cs` |
| Read-only diagnostic eligibility and production rejection regressions | `spikes/WindowsObservation/Compatibility/SelfTests.cs`, `spikes/WindowsObservation/CompatibilityGuardTests/Program.cs` |
| Policy design and minimal owner commands | `docs/architecture/explicit-switching.md`, `docs/architecture/windows-app.md`, `docs/validation/pr10-explicit-switching.md` |
| Probe boundaries and procedure links | `spikes/VirtualDesktop/README.md`, `spikes/WindowsObservation/README.md`, `spikes/WindowsObservation/Compatibility/README.md` |
