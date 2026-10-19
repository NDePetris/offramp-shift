# PR 10 — controlled 26300-family integrated owner testing

Follow-up: the owner reports integrated switching works but the first click can
appear to activate only. See the [focused correction and owner acceptance](pr10-first-click.md)
for the current patch, pinned-window limitation and explicitly deferred accounting decision.

## Owner evidence and decision — October 19, 2026

Preparation revision: `de869b5ed23578293e586fa2c19346305d223879`, branch
`codex/pr10-explicit-switch-to-work`. The owner reported Windows 11 26H2,
**26300.9711/x64**, and the following deliberately owner-operated results:

| Exercise | Owner-reported evidence |
|---|---|
| Read-only Personal | PASS; expected desktop matched; clean worker exit |
| Read-only Work | PASS; expected desktop matched; clean worker exit |
| One native Personal → Work | ConfirmedSuccess; visibly reached the correct desktop; no observed Explorer or window instability |

The agent did not perform or independently reproduce those live exercises.
Separate switching cleanup details, display layout/DPI, binary/source digests,
privilege/session details and end-to-end timings were not supplied with this
report; do not invent them or transfer historical 26200 hardware facts here.
The owner explicitly **accepted this limited evidence for controlled, opt-in
integrated application testing**. No further standalone switching repetitions
are required first. This is the documented compatibility decision superseding the
preparation-only 26300 restriction, not broad reliability or accessibility acceptance.

## Current implemented boundary

| Capability | Support / consent |
|---|---|
| Production read-only observation | Exact 26200.9457/X64 and 26200.9711/X64, plus numeric 26300.UBR/X64; observation consent and existing setup/health safeguards remain required |
| Application Switch to Work | Exact 26200.9457/X64 plus numeric 26300.UBR/X64; independent explicit default-off switching consent and fresh runtime authorization |
| Standalone switching qualification | Still exact 26200.9711/X64 or 26300.9711/X64; no family-wide diagnostic opt-in |
| Original VirtualDesktop spike | Still exact 26200.9457/X64 |
| Automatic switching | NO-GO |

OS and process must both be x64 before native activation, including rejecting x64
emulation on ARM64. Unknown families, malformed versions, bare 26300/prefix lookalikes
and unsupported architectures reject. Valid routine 26300 cumulative updates need
no code/approval change. This intentionally accepts undocumented ABI drift risk
within that family for this owner's experiment; a known regression can still require
a focused fail-closed patch. No universal Windows or unattended-use claim is made.

Settings v5 adds `SwitchingConsentFamily`, blank by default. On explicit UI opt-in
on 26300, it becomes `26300`, SwitchingConsent becomes true and the exact consent
build field is empty. Old v1–v4 settings migrate switching **off**, preserving
other preferences; old exact consent never silently becomes family consent. The
strict codec rejects invalid scopes and malformed legacy consent. Load alone
never rewrites settings; the next explicit save writes v5 atomically. An older
binary treats v5 as incompatible; do not downgrade or hand-edit the settings.

Observation and switching consent remain independent. Saved/current 26300 UBRs
share observation consent scope, and a newly granted family switching consent
survives those UBR updates. Full build/UBR stays in settings/runtime evidence.
Different families require new observation review and a new switching decision.
On startup with a mismatched switching scope, consent is durably cleared before
opening accounting; failure to save revocation aborts startup. Once cleared, it
cannot revive when returning to 26300. Reassignment and explicit setup review
also revoke switching consent. Healthy GUID presence/context and all runtime
checks remain mandatory on every switch, regardless of saved consent.

The bounded STA worker, short authorization lease, atomic single-use consumption,
configuration/session/shell/foreground/topology checks, one native call, result
verification, worker cleanup, uncertainty classification and nudge lockout are
unchanged. No retry, rollback, window relocation, synthetic keyboard input,
automatic policy or new nudge trigger was added. Core accounting and checkpoint
formats were not changed.

## Owner-run integrated procedure — NOT RUN by the agent

This test runs actual observation and Estimated accounting through the existing
WPF/tray app. Use an ordinary unelevated x64 Windows PowerShell session outside
the tool sandbox. Save work. Exit any older Shift instance through its tray and
wait for shutdown; the single-instance path otherwise activates the old process.
Do not start a headless monitor against the same store. Do not restart Explorer,
change desktop topology, delete identities or run a broad repetition campaign.

### 1. Review the exact tree and launch the rebuilt application

The owner authorized a focused local commit after the final scope check. Use the
**exact integrated implementation commit SHA in the final commit report**, not
preparation revision `de869b5` alone. Start from a clean reviewed checkout and
retain that revision and binary hashes as provenance. No raw GUIDs or settings
belong in logs, transcripts, screenshots or committed evidence.

No-COM preparation commands, from the repository root:

```powershell
Set-Location G:\Projects\offramp-shift
git branch --show-current
git rev-parse HEAD
git status --short --untracked-files=all
git diff --check
if (@(git status --porcelain).Count -ne 0) { throw 'Stop: reviewed checkout must be clean' }
$env:DOTNET_ROOT = (Resolve-Path .tools/dotnet).Path
.tools/dotnet/dotnet.exe --version
./run.ps1 build
if ($LASTEXITCODE -ne 0) { throw 'Stop: application build failed' }
$appDir = (Resolve-Path src/OfframpShift.App/bin/Release/net10.0-windows).Path
Get-FileHash -LiteralPath @((Join-Path $appDir 'OfframpShift.App.dll'), (Join-Path $appDir 'OfframpShift.Switching.dll'), (Join-Path $appDir 'OfframpShift.WindowsObservation.dll')) -Algorithm SHA256
```

Require branch `codex/pr10-explicit-switch-to-work`, the exact integrated commit
from the final report (a descendant of `de869b5`), a clean tree, pinned SDK
10.0.401/latest patch, successful build and
Windows Settings independently showing 26300.9711/x64 for the initial integrated
test. Later numeric UBRs in the approved family are eligible but have no individual
live PASS recorded here. Review any subsequent working-tree changes separately before use.

**Owner-run launch: this can activate real observation and resume existing enabled
monitoring intent.** Start only after accepting this integrated test:

```powershell
& (Join-Path $appDir 'OfframpShift.App.exe')
```

The supported combined build/launch alternative is `./run.ps1 app`; do not run both
or rely on an older process. Keep the actual tested source/binary provenance with
the result. No standalone qualification command is required again.

### 2. Complete/review setup while switching stays off

If monitoring is already active, use tray **Pause** before changing setup. Open
**Settings → Desktop setup**. If monitoring consent needs review after the feature
upgrade, click **I agree — enable experimental observation**. This consent is for
observation/Estimated monitoring, not switching. Existing monitoring intent may
resume only through its existing safeguards; keep Pause while reviewing.

Independently identify Personal and Work in Task View. Verify the existing role
statuses are valid and the current context agrees. If assignments need correction,
manually select each desktop and use the tray's **Use current desktop as Personal**
or **Use current desktop as Work**, without first opening the main window on the
other desktop. The app captures fresh GUIDs. Assignment stops monitoring and
revokes switching consent. Never replace a missing GUID by an index/name guess.

In Settings, choose **Reminder style → Interactive window** and click **Save
reminder preference**. Keep **Allow an explicit Switch to Work attempt from a
nudge** unchecked, then click **Save switching consent** and require success.
The family scope text must identify Windows family 26300 on x64. If saving fails,
stop; a checkbox edit alone is not an acknowledged preference change.

### 3. Reach a normal nudge; verify consent-off behavior and existing controls

Manually select Personal, then choose **Start monitoring** (or Resume for an already
started, paused session). Use ordinary counted Personal applications with fresh,
healthy monitoring. Do not inject activity or force focus. Check Home for saved
usage, allowance and current context. Unknown/stale/idle/neutral activity does not
advance counted Personal time; a provider or persistence fault is a stop condition.

There is **no supported Show nudge now/test-switch CLI**. The normal method is to
use Personal until the **saved allowance is exhausted**, then accept the ordinary
nudge presentation. Defaults are 30 minutes of allowance and a one-minute grace
period; PR 9's interactive style prompts immediately on accepted exhaustion while
grace remains Core state, and later repeats use counted Personal time. Existing
saved settings/usage govern the actual wait. Do not lower thresholds, edit SQLite,
reset usage or add a trigger just for convenience. Real notification timing remains
best effort and the earlier PR 9 timing uncertainty is not resolved by this patch.

At a valid interactive nudge, confirm **Switch to Work** is disabled and its reason
mentions consent. It must not start progress or a native attempt. Manual fallback
text remains available. If presentation/focus is rejected or the window closes
because context changed, record it; do not force foreground through a tool.

On separate naturally occurring nudges, while switching is still off:

1. Click **Add 1 minute** once. Require acknowledged save and normal closure;
   continued counted Personal use should consume that extension before another
   ordinary nudge becomes eligible. Saved usage continues; it is not reset.
2. On a later nudge, click **Add 5 minutes** once and check the same behavior for
   five minutes of counted Personal activity. Work/away time does not consume it.
3. On a later nudge, dismiss with **X** (or the existing Escape/Alt+F4 route).
   Dismissal uses the existing one-minute durable deferral; the window remains
   open on failed authorization/save and reports failure. It is not a silent reset.

These actions intentionally exercise existing Core deferral behavior; the patch
does not change it. They require several nudges and may take at least seven extra
minutes of counted Personal use beyond reaching exhaustion. Do not treat elapsed
wall time as measured accounting accuracy. A genuine completed Work/idle break
can reset allowance and lengthen the wait; do not change the break threshold to
avoid that normal behavior.

### 4. Explicitly enable switching and test exactly once

Open Settings and check **Allow an explicit Switch to Work attempt from a nudge**,
then click **Save switching consent**. Require an acknowledged save and scope
**Windows build family 26300 on x64**. This is the new explicit family opt-in;
monitoring consent or migrated exact consent is insufficient.

Saving preferences invalidates the old nudge/presentation. Return to Personal and
wait for a **new** naturally eligible interactive nudge; do not expect an old
window to regain authorization. Healthy foreground/current Personal context,
valid bound GUIDs and a fresh presentation remain required. If the action remains
unavailable, retain the reason and stop instead of bypassing it.

Click **Switch to Work once**. Do not double-click or retry. Require a confirmed
result and independently verify actual Work in Task View, with no transient wrong
destination, window movement/minimization or Explorer instability. The nudge may
close normally and the app's main window may remain on Personal; inspect retained
feedback after returning **manually** when safe. No automatic return is performed.
Inspect diagnostic-process cleanup locally; do not copy command lines/GUIDs into
evidence. Record an uncertain result as uncertain even if Task View happens to
show Work afterward.

### 5. Disable switching and verify manual fallback

After the one attempt, uncheck **Allow an explicit Switch to Work attempt from a
nudge** in Settings and click **Save switching consent**. Require save success.
On a later natural nudge, switching must again be unavailable. Use **Win+Tab** and
choose Work **manually** to verify the existing fallback, then return manually as
needed. Do not perform a second native switch for this checklist.

**If anything unexpected happens:** stop testing immediately. Use tray **Pause**
to stop accounting/interventions and revoke unconsumed authorization. Disable and
save switching consent through Settings; use tray **Exit** if the UI/monitoring is
uncertain or saving fails. A consumed shell request may still complete late;
Pause/Exit/worker termination cannot undo it. Inspect Task View manually. Do not
retry, compensate with a reverse switch, restart Explorer or alter topology.
Indeterminate results retain the current nudge's lockout. Report leftover owned
workers or shutdown/cleanup failures rather than assuming success.

## Evidence to retain and remaining limits

Return sanitized PASS/FAIL/BLOCKED/NOT RUN for consent-off availability, explicit
opt-in, one actual transition, independent Task View result, extensions, dismissal,
manual fallback, disable behavior, worker cleanup, source/build/hardware and any
unexpected focus/window effects. Record actual display count/layout/scaling and
privilege/session facts for the integrated run; they are not supplied by the earlier
brief report. Keep raw GUIDs, process lists, titles/content and settings private.

Still unverified: integrated WPF native behavior on this patch, broad cumulative
update compatibility, Explorer recovery, races/topology deletion, reboot behavior,
multiple monitors/mixed DPI, fullscreen/elevated applications, Narrator/full keyboard/
high contrast, long-running monitoring/accounting accuracy and unattended use.
PR 5 Tests 8–12 remain deferred; issues #8/#9 and PR 9 timing uncertainty remain.
Process isolation cannot protect Explorer from every undocumented ABI fault.
Automatic switching and production browser attribution remain **NO-GO**.

## Automated validation

All seven prescribed commands passed on the final implementation with SDK
**10.0.401**, warnings as errors; `git diff --check` also passed. Logs are local
and ignored under `artifacts/pr10-family/*-final.log`. Final reporting edits are
documentation only; tested implementation sources remain unchanged.

| Command | Final automated result |
|---|---|
| `./run.ps1 test` | PASS / exit 0: Core 82 tests / 38,755 assertions; Persistence 83 / 772; Host 274 / 2,391; WPF 78 / 242; zero failures. `root-test-final.log` |
| `./run.ps1 format` | PASS / exit 0, verify-no-changes. `root-format-final.log` |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS / exit 0: 58 fake-policy cases including 1,000 topology permutations, plus three owned non-COM isolation checks. `virtual-desktop-final.log` |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS / exit 0: 2,053 policy/protocol assertions, five extension/manifest tests, synthetic transport/process tests. No browser matrix. `browser-attribution-final.log` |
| `./spikes/WindowsObservation/run.ps1 test` | PASS / exit 0: 41 tests / 10,282 assertions; synthetic observation and real disposable SQLite. `windows-observation-final.log` |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS / exit 0: 24 unsupported build/process/OS-architecture entry cases with zero activation, presentation decisions, 16 diagnostic groups including owned non-COM workers. `compatibility-final.log` |
| `./spikes/WindowsObservation/run.ps1 format` | PASS / exit 0, verify-no-changes. `observation-format-final.log` |

Focused regressions cover multiple numeric UBRs, malformed versions, other families,
unsupported OS/process architecture pairs, independent observation/switching/probe/
diagnostic eligibility, all legacy settings shapes, default-off migration, explicit
opt-in, consent-off rejection, cumulative-update retention, durable revocation on a
major family change, failed revocation save, reassignment, fresh single-use grants,
one fake native attempt and unexpected-destination indeterminate classification.
Existing WPF lockout, worker isolation and accounting regressions remain intact.
Providers/native sessions are synthetic; SQLite and owned non-COM processes are
real. Automated success is not live shell compatibility or accounting certification.

All live integrated exercises above remain owner-run and NOT RUN by the agent.
A local commit was authorized following the final scope check. No push, PR, merge, deployment or release is authorized.

## Exact changed-file inventory

All paths below are repository-relative; new test/document files are included.
No Core/Persistence implementation or checkpoint schema file changed.

| Purpose | Files |
|---|---|
| Switching family predicate and separate original spike gate | `src/OfframpShift.Switching/Switching.cs`, `src/OfframpShift.Switching/NativeSession.cs` |
| Observation family predicate, consent scope and OS guard | `src/OfframpShift.WindowsObservation/Evidence.cs`, `src/OfframpShift.WindowsObservation/DesktopWorker.cs` |
| v5 consent migration and host enforcement | `src/OfframpShift.ApplicationHost/WindowsSettings.cs`, `src/OfframpShift.ApplicationHost/MonitoringApplication.cs` |
| Supported Settings interface/copy | `src/OfframpShift.App/MainWindow.xaml`, `src/OfframpShift.App/MainWindow.xaml.cs` |
| Application regressions and strict legacy fixtures | `tests/OfframpShift.ApplicationHost.Tests/FamilyCompatibilityRegressions.cs` (new), `Program.cs`, `SwitchRegressions.cs`, `SwitchPolicyRegressions.cs`, `SwitchWorkerGuardRegressions.cs`, `Pr8Regressions.cs`, `InteractiveHostRegressions.cs` (all in the same test directory) |
| Observation/diagnostic policy regressions and accurate no-COM support report | `spikes/WindowsObservation/Tests/Program.cs`, `spikes/WindowsObservation/Compatibility/SelfTests.cs`, `spikes/WindowsObservation/Compatibility/Program.cs`, `spikes/WindowsObservation/CompatibilityGuardTests/Program.cs` |
| Current decisions, historical boundaries and owner instructions | `AGENTS.md`, `README.md`, `docs/architecture/explicit-switching.md`, `docs/architecture/windows-app.md`, `docs/validation/pr10-explicit-switching.md`, `docs/validation/pr10-integrated-26300.md` (new), `spikes/VirtualDesktop/README.md`, `spikes/WindowsObservation/README.md`, `spikes/WindowsObservation/Compatibility/README.md` |

## Final scope check and local commit

PASS: all 28 changed files, including the two new files, are part of the approved
26300-family owner-testing change, its consent/schema migration, associated guards,
regressions, UI copy, or current/historical documentation. Production observation
and explicit application switching compatibility changes are intentional; they
implement the owner's accepted limited-evidence decision. Other build families
remain unsupported apart from the preserved exact 26200 approvals. Standalone
qualification and original spike gates remain separate. Automatic switching is NO-GO.

No unrelated files, Core/Persistence implementation changes, checkpoint changes,
monitoring-threshold changes, new native operations, telemetry, distribution or
licensing changes are included. Final scope review changed only this documentation;
the implementation still matches the passing seven-command validation above.
`git diff --check` passed again. No additional live OS exercise was performed.
Local commit subject: `feat: enable controlled Windows 26300 owner testing`.
