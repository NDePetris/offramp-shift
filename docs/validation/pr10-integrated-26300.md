# PR 10 — controlled 26300-family integrated owner testing

**Final owner acceptance:** integrated switching and first-click behavior passed
at `7de47ba6e4965db7443a0a3fb97588688122d046`. See the bounded acceptance matrix
below and the [first-click investigation](pr10-first-click.md). Historical setup,
qualification and automated evidence remains labeled separately.

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

## Final owner acceptance and PR review — October 19, 2026

**Recommendation: READY WITH DOCUMENTED LIMITATIONS.** All seven automated
closeout checks passed. The owner has accepted the primary integrated
interaction; no additional standalone repetitions or broad accounting campaign
are prerequisites for PR review. This is not approval to merge, release or use
the application unattended.

Owner-reported environment: Windows 11 26H2 **26300.9711/x64**, **.NET SDK 10.0.401**;
primary **3440×1440 landscape at 100%**, secondary **2160×3840 portrait at 150%**.
The first-click test revision is **`7de47ba6e4965db7443a0a3fb97588688122d046`**.
These are supplied environment facts, not proof that each monitor was exercised.
No native exercise was performed or independently reproduced by the agent.

| Capability / exercise | Owner evidence | Boundary |
|---|---|---|
| Read-only Personal observation | **PASS** | Standalone qualification at `de869b5`; expected endpoint matched, clean worker exit |
| Read-only Work observation | **PASS** | Same standalone evidence; independent from switching approval |
| One explicit Personal → Work diagnostic | **PASS** | `ConfirmedSuccess`, visibly correct destination; earlier report found no Explorer/window instability |
| Integrated Switch to Work | **PASS** | Owner confirmed the integrated action reaches Work; standalone success alone was not used to infer this |
| First-click integrated switching | **PASS** | At `7de47ba`, worked on the first attempt without a preliminary activation click |
| Extensions and dismissal, aggregate prior testing | **PASS — prior owner report** | Retained as supplied; does not prove every duration/route on the final revision |
| Automatic nudge closure after the final switch | **NOT CONFIRMED** | Successful switching is not separate closure evidence |
| Explorer/window stability during the final first-click run | **NOT CONFIRMED** | Earlier standalone stability observation is not transferred to this run |
| Secondary-monitor presentation / complete mixed-DPI acceptance | **NOT CONFIRMED** | Display configuration supplied; no separate PR 10 per-monitor result |
| Full keyboard interaction, high contrast, Narrator | **NOT CONFIRMED** | Earlier PR 5/9 evidence and automated WPF tests do not establish new PR 10 acceptance |
| Separate Add 5 minutes, Escape and Alt+F4 on the final revision | **NOT CONFIRMED** | Aggregate extension/dismissal report does not enumerate these variants |
| Recovery, long-duration accuracy, reboot/topology race matrix | **NOT RUN for this closeout** | Existing issues and PR 5 deferrals remain |
| PR 5 Tests 8–12 | **NOT RUN — deferred** | Prior owner scope decision preserved |
| Automatic switching | **NO-GO** | Not implemented or approved |

### Complete implementation review

The initial checkout was clean on `codex/pr10-explicit-switch-to-work`, HEAD
`7de47ba6e4965db7443a0a3fb97588688122d046`. Local `main` and the merge base are
`63c3f075148a69fd36e95553b86f5985e7859cfa`; GitHub independently confirms
[PR #18 (PR 9)](https://github.com/NDePetris/offramp-shift/pull/18) is merged at
that commit. The complete 58-file comparison includes all four PR 10 commits:
`45c0d73`, `de869b5`, `83ab7a6`, `7de47ba`, rather than only the latest fix.

Review found **no concrete safety defect blocking PR review**. Changes are limited
to shared switching extraction/protocol/worker supervision, host/WPF authorization
and consent, independently gated compatibility diagnostics and the approved family
policy, related tests/project references and documentation. COM ABI slots are
preserved; lockfile changes add only the shared project references. No Core,
Persistence, ActivityMonitoring or BrowserAttribution implementation change appears
against main; SQLite checkpoint semantics and accounting thresholds are unchanged.

Verified safeguards: explicit default-off switching consent is independent from
observation consent; strict v1–v4 migration never opts into switching. Numeric
26300 UBRs on x64 share only the deliberately approved family scope; a major-family
change revokes consent and cannot revive it on return. Observation retains both
approved exact 26200 builds; application switching retains exact 26200.9457, while
26200.9711 remains observation/diagnostic-only. Standalone qualification remains
exact-build gated, and the original spike gate remains separate.

Each request requires a strictly confirmed nudge, healthy fresh Personal context,
valid distinct GUID bindings, matching shell/session/configuration/topology and a
fresh atomic single-use authorization. The bounded STA child makes at most one
native call and verifies the result; no retries, rollback, window relocation or
synthetic input are added. Indeterminate results retain nudge lockout, retained
feedback and manual Task View fallback. Existing extensions/dismissal retain their
shared submission exclusion and durable acknowledgment. Saving disabled switching
consent, Pause and Exit revoke unconsumed authorization; none undo a consumed call.

The first-click capture is conditional and read-only. Requested HWND must equal
the previously confirmed nudge HWND before and after capture, and sampled foreground
must equal that same HWND. Events drained on both sides revoke external transitions,
including leave-and-return. Sampler checks foreground/session stability; the switch
worker independently validates again. The capture calls no estimator reset/Point
or durable Core command. Existing event draining still informs accounting normally.
Read-only worker methods expose no state-changing desktop operation.

### Limits retained for review

- Pinned/cross-desktop foreground windows can fail strict membership validation;
  the owner's CandidateWrongDesktop report disappeared with a normal unpinned app.
  No identity exception, window movement or automatic acceptance is added.
- Family eligibility intentionally survives UBR updates, but undocumented COM ABI
  drift can still break compatibility. Only limited 26300.9711 live evidence exists;
  every UBR is not independently certified.
- A successful verification point or worker termination cannot rule out delayed
  shell effects. Check/call races and Explorer faults cannot be made atomic here.
- Observation COM reply wait is 2 seconds, cleanup exit wait 1 second; collections
  over 500 ms and evidence over 2 seconds old reject. Synchronous local Windows
  queries/pipe writes remain outside the reply deadline, so the full capture has
  **no absolute end-to-end deadline** and can delay the monitoring owner.
- Explorer recovery ([#8](https://github.com/NDePetris/offramp-shift/issues/8)),
  sustained monitoring/accuracy and the accepted PR 9 timing uncertainty
  ([#9](https://github.com/NDePetris/offramp-shift/issues/9)) remain unresolved.
  These are related follow-ups, not closed by the switching implementation.
  This review does not identify a separate switching issue to close automatically.
- Full Narrator, high contrast, keyboard/mixed-DPI acceptance and PR 5 Tests 8–12
  remain at their recorded unconfirmed/deferred status. No recovery, accessibility,
  accounting or unattended-use certification is implied.

Separate agreed accounting follow-up, **not implemented in PR 10**: verified,
unlocked time on Personal should count as usage regardless of input inactivity;
idle-on-Personal should not earn break credit. No classification, idle/break
threshold, checkpoint, allowance, playback detection or reset behavior is changed.
Automatic switching and production browser attribution remain **NO-GO**.

### Final automated closeout validation

All commands were rerun on the complete implementation at `7de47ba` with pinned
SDK **10.0.401**, warnings as errors. Exit codes were **0** throughout; subsequent
edits change only these three validation documents and one stale probe README
statement that incorrectly claimed no 26300 live evidence. No live observation/switching
command, integrated test, desktop manipulation or Explorer restart was performed.

| Command | Closeout result |
|---|---|
| `./run.ps1 test` | **PASS**: Core **82 tests / 38,755 assertions / 0 failures**; Persistence **83 / 772 / 0 failures**; host **283 / 2,438 / 0 failures**; WPF **80 / 242 / 0 failures** |
| `./run.ps1 format` | **PASS**, verify-no-changes |
| `./spikes/VirtualDesktop/run.ps1 test` | **PASS**, **58 tests / 0 failures**, including 1,000 topology permutations; **3** owned non-COM isolation checks passed |
| `./spikes/BrowserAttribution/run.ps1 test` | **PASS**, **2,053 assertions**, **5 extension tests / 0 failures**, synthetic process/pipe transport passed |
| `./spikes/WindowsObservation/run.ps1 test` | **PASS**, **41 tests / 10,282 assertions / 0 failures** |
| `./spikes/WindowsObservation/compatibility.ps1 test` | **PASS**, **24** unsupported-entry cases and exact/family matrices; **16 diagnostic groups / 0 failures**, synthetic/owned processes, no COM |
| `./spikes/WindowsObservation/run.ps1 format` | **PASS**, verify-no-changes |
| `git diff --check` | **PASS**, including final documentation edits |

Logs are local and ignored under `artifacts/pr10-closeout-*.log`. These are
deterministic/simulated provider, WPF routing, disposable SQLite and owned non-COM
process results, not new live compatibility or accounting evidence. Worker
deadlines/cleanup, independent gates, migration/default-off consent, single-use
grants, unrelated foreground rejection, first-click capture, one native attempt,
indeterminate lockout and existing extension/dismissal regressions remain covered.

The final closeout commit uses `docs: record PR 10 owner acceptance and limitations`.
Its SHA is reported separately to avoid embedding a self-referential hash; the
live first-click provenance remains `7de47ba`, not this documentation-only commit.
No push, remote PR creation/modification, merge, deployment or release is authorized.

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
privilege/session facts for any further integrated run. The final report above
supplies layout/scaling, but no complete per-monitor or privilege/session matrix.
Keep raw GUIDs, process lists, titles/content and settings private.

The integrated action and first-click behavior now have the bounded owner PASS
above. Still unverified: final automatic closure and stability, broad cumulative
update compatibility, Explorer recovery, races/topology deletion, reboot behavior,
secondary-monitor/full mixed-DPI acceptance, fullscreen/elevated applications,
Narrator/full keyboard/high contrast, long-running monitoring/accounting accuracy
and unattended use.
PR 5 Tests 8–12 remain deferred; issues #8/#9 and PR 9 timing uncertainty remain.
Process isolation cannot protect Explorer from every undocumented ABI fault.
Automatic switching and production browser attribution remain **NO-GO**.

## Historical family-enablement automated validation — `83ab7a6`

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

## Historical family-enablement changed-file inventory — `83ab7a6`

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

## Historical family-enablement scope check and local commit — `83ab7a6`

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
