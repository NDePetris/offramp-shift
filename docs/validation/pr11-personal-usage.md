# PR 11 — Personal usage accounting

## Scope and provenance

Branch `codex/pr11-personal-usage-accounting` starts at refreshed `origin/main`
`d30c620` (merged PR 10 / GitHub #19), with a clean working tree. This is the
planning label PR 11, separate from historical GitHub PR #11 / PR 5 acceptance.
The initial implementation request withheld commit/push/PR authorization. The
October 21, 2026 closeout request separately authorizes a cohesive commit, branch
push and PR against main after a nonblocking review and passing required checks.
Merge, deployment and release remain unauthorized.

| Evidence | Previous classification | PR 11 classification |
|---|---|---|
| Eligible unlocked Personal, active input | Counted Personal | Counted Personal |
| Eligible unlocked Personal, idle / qualified idle | Idle; potentially qualifying break | Counted Personal; no break |
| Other / excluded Personal, idle | Idle; potentially qualifying break | Neutral; no usage or break |
| Verified Work, active or idle | Work break | Work break |
| Verified lock, known scope | Locked break | Locked break |
| Unknown, unavailable, conflicting, unsupported, suspended, paused | No credit | No credit |

Existing inclusion/exclusion rules, confidence/accounting modes and identity
requirements still apply. Input Unknown fails closed for unlocked observations.
The legacy Idle ActivityKind and input/state fields remain compatible public and
serialized shapes, but the classifier no longer produces an idle-only break.
No Core branch awards idle-only credit. IdleProgressMs remains diagnostic state,
not earned break time; restoration discards unfinished idle/break progress.
Historical completed resets and latches remain intact, without reconstruction.
Checkpoint v3, SQLite schema 1, settings v5 and estimation policy version 1 remain
unchanged; no migration is necessary because field shapes/invariants are valid.

The estimator no longer splits/suppresses Personal intervals at inactivity or
ordinary input resumption. It keeps input-health/discontinuity validation,
foreground/session/GUID/topology/shell checks, monotonic timing, power handling,
receipt freshness, holdback, bounded intervals, queue invalidation and delivery
acknowledgments. Matching snapshots remain estimates, never certification.
Single serialized monitoring ownership and exact durable pending-commit/retry
behavior are unchanged. Reminder eligibility, focus authorization, explicit
switching consent/authorization and build gates are unchanged. Idle effects are
committed on Core cadence but suppressed by presentation checks without replay.

## Deterministic regression coverage

The existing Core fixtures now cover prolonged Personal inactivity beyond both
thresholds, zero/large inactivity thresholds, partial Work break interruption,
application exclusions/Other, all known input variants, Work/lock reset latching,
legacy and interactive cadence, and idle consumption of 1/5-minute extensions.
The existing estimator harness covers already-idle startup after a fresh baseline,
threshold crossing and ordinary input return without unknown splits; it retains
late events, health loss, foreground instability, queue/clock/power/suspend/gap
and rejection tests. SQLite regressions restore obsolete idle progress without
break credit and consume restored extensions during idle without downtime credit.
Host regressions for both reminder styles run unattended Personal across repeated
Core nudges, checking counted usage, zero break, no mailbox delivery and no input
resumption replay. Existing storage-failure, duplicate/overlap, pause/resume,
locked/unlocked, switching authorization and WPF lifecycle coverage remains.

## Automated validation

All seven checks passed (exit 0), with pinned SDK **10.0.401**, warnings as errors.
Logs and exit records are local and ignored under `artifacts/pr11-*`.

| Required command | Automated result | Log in artifacts/ |
|---|---|---|
| `./run.ps1 test` | PASS: Core 82 / 40,598 assertions; Persistence 84 / 778; Host 285 / 2,960; WPF 80 / 242; zero failures | `pr11-root-test-final.log` |
| `./run.ps1 format` | PASS: verify-no-changes | `pr11-root-format.log` |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS: 58 fake-provider cases and three owned non-COM isolation checks | `pr11-virtual-desktop.log` |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS: 2,053 policy/protocol assertions, five extension tests, synthetic transport/process checks | `pr11-browser-attribution.log` |
| `./spikes/WindowsObservation/run.ps1 test` | PASS: 41 tests / 10,300 assertions, zero failures | `pr11-observation.log` |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS: 24 unsupported-entry cases, presentation decisions and 16 diagnostic groups; zero failures | `pr11-compatibility.log` |
| `./spikes/WindowsObservation/run.ps1 format` | PASS: verify-no-changes | `pr11-observation-format.log` |

`git diff --check` passed. The application suite includes the focused Core,
SQLite recovery/extension and both-style unattended-host regressions above;
the observation suite includes the focused estimator startup/input-return test.
Initial sandbox build stopped without compiler diagnostics. An unrestricted retry
passed Core/Persistence/Host but encountered live-app DLL locks at WPF build.
Final application test/format used process-local MSBuild environment properties
`UseArtifactsOutput=true` and `ArtifactsPath=<repo>/artifacts/pr11-build` to isolate
both output and intermediates. Runners and build configuration were not edited;
existing live app processes were not stopped or relaunched. Probe checks used
the normal output paths. No native switching or live observation exercise ran.
Automated passes establish only deterministic behavior, local SQLite and owned
non-COM worker checks, not actual Windows accounting accuracy or readiness.

## Changed files

| Responsibility | Repository paths |
|---|---|
| Classification and idle-break removal; compatible state retained | `src/OfframpShift.Core/Model.cs`, `MonitoringCoordinator.cs` |
| Bounded inactivity-independent intervals | `src/OfframpShift.ActivityMonitoring/IntervalEstimator.cs` |
| Host status copy | `src/OfframpShift.ApplicationHost/MonitoringApplication.cs` |
| Settings labels/help/validation and extension help | `src/OfframpShift.App/MainWindow.xaml`, `MainWindow.xaml.cs`, `NudgeWindow.xaml` |
| Core policy/cadence/extension/latch regressions | `tests/OfframpShift.Core.Tests/Program.cs`, `BestEffortRegressions.cs`, `InteractiveRegressions.cs` |
| Durable latch/legacy state/idle extension regressions | `tests/OfframpShift.Persistence.Tests/Program.cs`, `InteractivePersistence.cs` |
| Both-style suppression and input-return regression | `tests/OfframpShift.ApplicationHost.Tests/Pr8Regressions.cs` |
| Estimator baseline/input-return regression | `spikes/WindowsObservation/Tests/EstimatorRegressions.cs` |
| Current architecture | `docs/architecture/core-monitoring.md`, `best-effort-monitoring.md`, `windows-app.md` |
| Current procedure and preserved historical provenance | `docs/validation/pr11-personal-usage.md` (new), `pr4-accounting.md`, `pr5-owner-acceptance.md`, `pr9-interactive-nudges.md` |

All abbreviated file names in a row share its stated directory. Persistence
implementation, schemas, settings versions, switching, observation allowlists,
native providers and notification presentation implementations are unchanged.

## Short owner acceptance procedure

This is the prepared procedure, not a claim that the owner executed these exact
steps. The separately supplied owner observations are recorded below.

Use an approved observation configuration: exact 26200.9457/x64 or
26200.9711/x64, or the separately approved 26300 family/x64 for controlled owner
evaluation. Record tested SHA, date, full Windows build/architecture, privileges,
monitor layout/scaling, chosen durations and reminder style. Keep identities,
GUIDs, titles/content, URLs and history private. Use disposable test data in a
separate standard Windows account if needed; preserve existing data. Exit older
app instances normally before launching the tested build. Do not enable switching
for this procedure; manually choose desktops through Task View.

1. Save practical short durations (for example allowance 2 minutes, break 1 minute,
   reminder inactivity 10 seconds). Save original settings for explicit restoration.
   On Personal, leave an eligible stable foreground app without keyboard/mouse
   input for more than both inactivity and break durations. Compare saved totals
   before/after: subsequent observed Personal time keeps accumulating, break stays
   zero, and no allowance reset occurs. Passive video is subject to the same policy.
2. With interactive reminders enabled, repeat the unattended interval through
   exhaustion and a repeat boundary. Require no unexpected focus-taking window.
   Resume input: require no immediate replay. Record any spontaneous presentation
   separately from committed Core effects; Windows notifications remain best effort.
3. Manually select Work. Observe a fresh baseline and a full qualifying break with
   either active or idle input. Require one reset; remaining on Work must not repeat
   it. Return to Personal manually and verify usage resumes.
4. Accumulate some Personal usage, then lock for a qualifying break on an awake
   machine. Unlock and inspect committed state: require one reset for verified
   locked time, without charging uncertain transition or suspended time. Repeat
   lock observations must not duplicate reset effects.
5. Pause, wait and resume: usage and break must not advance while paused, and
   resumption requires fresh evidence. If unavailable evidence occurs naturally
   or in an already reviewed fixture, require no usage/break credit for that gap.
   Do not improvise Explorer/ACL/clock faults; mark unavailable exercise NOT RUN
   when no suitable fixture is available.
6. Restore original durations and reminder preferences, then Exit normally.
   Report PASS/FAIL/BLOCKED/NOT RUN per check, expected/observed totals, environment
   and cleanup outcome. Stop on unexpected behavior; do not retry switching or
   start a multi-hour accounting campaign.

At initial implementation closeout, all real-Windows PR 11 owner acceptance was
**NOT RUN**. The October 21 report below supersedes that pending status only for
the explicitly reported scenarios. Existing PR 5 Tests 8–12 remain deferred; Explorer recovery (#8),
monitoring reliability/accuracy (#9), silent stale-provider/event completeness,
fullscreen/elevated/accessibility and sustained/unattended limitations remain.
Automatic switching and production browser attribution remain NO-GO. No media
awareness is implemented: unattended unlocked eligible Personal time intentionally
counts even when no person is present.

## Final review and owner report — October 21, 2026

Review source: the complete existing working-tree diff, including this previously
untracked report, against freshly fetched `origin/main` at
`d30c620b4f173e5af4629e310b768ef6dc8dc2e7`. No uncommitted work was discarded.
No blocking accounting or safety defect and no reproducible PR 11 presentation
regression was identified. No product code or tests changed during closeout.
Documentation records the new evidence, replaces obsolete authorization/status
statements and repairs inserted non-UTF-8 punctuation in this report and the
PR 5 historical-policy note. Historical test outcomes remain intact.

The full scope review confirms that classification and estimation implement the
approved policy while retaining application rules, unknown-input failure,
Work/lock precedence, pause/suspend handling, bounded transient uncertainty,
baseline/receipt/holdback/queue guards and single-owner durable accounting.
Only Work/lock reaches break earning. Partial legacy idle progress cannot earn
credit after restore; saved totals, exhaustion, extensions and completed reset
latches remain compatible. Idle observations/native last-input capability remain
available. There are no schema/migration, dependency, permission, telemetry,
network-service, browser-attribution, media-detection, compatibility-allowlist,
explicit-switch authorization/consent or automatic-switching changes.

### Owner-supplied observations

Source: the owner's final-review request, received October 21, 2026. The request
does not supply tested SHA, exact Windows build, hardware, measured timings or
an exact step transcript. None is inferred from prior PRs or this agent's host.
These observations are owner-reported real-Windows evidence, not agent-executed
or independently certified acceptance.

| Scenario | Supplied status / limit |
|---|---|
| Personal usage continues during input inactivity | **PASS — owner-reported** |
| Idle on Personal does not reset the allowance | **PASS — owner-reported** |
| Work break accumulates credit and resets allowance | **PASS — owner-reported** |
| Locked-session break | **NOT RUN / not separately confirmed** |
| Idle nudge suppression | Observed; exact behavior not independently certified |
| Active interactive nudge presentation | Inconsistent behavior reported; focused source/regression review below, live cause unresolved |
| Sustained accounting accuracy and unattended reliability | **NOT VALIDATED** |

Paused/unavailable behavior, exact repeat/extension timings and broader native,
accessibility/monitor/recovery cases receive no additional live PASS from this
report. PR 11 is **not fully real-Windows validated**.

### Focused nudge presentation disposition

`MonitoringRuntime`, `BreakReminderMailbox`, `NudgePresenter`, `NudgePlatform`,
the Dispatcher consumption path and PR 9/10 focus safeguards are unchanged from
the reviewed base. PR 11 changes which verified intervals advance Core usage and
repeat/extension remainders; a committed nudge still does not guarantee a window.

| Reported behavior | Disposition and evidence |
|---|---|
| Missing windows while Personal is unattended | **Expected suppression is consistent with the report.** `EligiblePersonal` and `HealthyPersonal` still require available, nondiscontinuous input with age strictly below the configured threshold. The report alone does not certify why each window was absent. |
| `Window shown; Windows denied focus.` / window behind others | **Existing documented Windows focus limitation.** After nonactivating show and strict completion, one ordinary `SetForegroundWindow` request returned false. The presenter keeps the window available; focus and frontmost visibility are not guaranteed. See [PR 10 focus-refusal guidance](pr10-first-click.md#retained-owner-procedure--not-run-by-the-agent) and [the presentation architecture](../architecture/windows-app.md#interactive-nudges-pr-9). No retry or topmost/focus bypass is authorized. |
| `Authorization rejected after show: FreshPersonalContextUnavailable` / no surviving window | **Existing fail-closed path; the reported occurrence's cause remains unresolved.** Completion captures fresh context and rejects when `CanKeepNudgeOpen` fails. That predicate combines storage/intervention, pause, health/freshness, desktop/topology/shell, active session, input capability/discontinuity/inactivity and foreground availability. This message alone cannot identify inactivity or prove a Windows focus refusal. The presenter closes the nonactivating candidate before its explicit focus request. |
| Active-use inconsistency | **Separate unresolved presentation/reliability observation; no demonstrated PR 11 regression.** No new eligibility, event consumption, expiry, scheduling or authorization path was introduced. Preserve the uncertainty; do not claim all active failures are explained or fixed. |

Delivery still uses only new committed effects, matching application/current
Personal evidence and fresh healthy context, rechecked after durable save and
queued signals. The single mailbox slot deduplicates sequences, waits for matching
interactive publication without extending expiry, consumes stale/rejected events
and never queues suppressed history. Input return does not regenerate a consumed
effect or zero the Core repeat remainder. Ordinary input return no longer creates
an idle-qualification accounting interruption; input discontinuities and native
foreground/session/desktop transitions remain conservative. A later newly due
effect may present if every current safety check passes.

The rerun includes existing active exhaustion/repeat cases for both styles,
publication ordering/expiry, strict completion despite the nudge's own foreground
event, genuine external-context rejection, simulated focus refusal and first-click
behavior. PR 11's unattended regression spans multiple committed nudges for both
styles and verifies zero delivery and no replay on input return. These checks use
synthetic evidence/presentation, not live focus reliability. No new campaign,
monitoring, scheduler, focus retry, synthetic input or safety bypass was added.

The earlier [PR 9 unresolved timing observation](pr9-interactive-nudges.md#unresolved-timing-observation--accepted-for-now)
already points to [#9 monitoring reliability](https://github.com/NDePetris/offramp-shift/issues/9)
as follow-up context. This review neither assigns the new reports a proven cause
nor claims that issue was updated. Issues #8/#9 remain independent of PR 11 scope.

### Final automated validation

All seven required commands were rerun for this closeout with pinned SDK
**10.0.401** and warnings as errors. Final results are recorded below; the earlier
implementation-run results above are retained separately. Application builds use
process-local `UseArtifactsOutput=true` and
`ArtifactsPath=<repo>/artifacts/pr11-closeout-build`; probe checks use normal output.
No active owner app was terminated and no owner settings were modified.

| Required command | Closeout result | Local ignored log in artifacts/ |
|---|---|---|
| `./run.ps1 test` | PASS / exit 0: Core 82 / 40,598 assertions; Persistence 84 / 778; Host 285 / 2,960; WPF 80 / 242; zero failures | `pr11-closeout-root-test.log` |
| `./run.ps1 format` | PASS / exit 0: verify-no-changes | `pr11-closeout-root-format.log` |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS / exit 0: 58 fake-policy cases and three owned non-COM isolation checks | `pr11-closeout-virtual-desktop.log` |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS / exit 0: 2,053 policy/protocol assertions, five extension tests and synthetic transport/process checks | `pr11-closeout-browser-attribution.log` |
| `./spikes/WindowsObservation/run.ps1 test` | PASS / exit 0: 41 tests / 10,300 assertions, zero failures | `pr11-closeout-observation.log` |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS / exit 0: 24 unsupported-entry cases, presentation decisions, 16 diagnostic groups | `pr11-closeout-compatibility.log` |
| `./spikes/WindowsObservation/run.ps1 format` | PASS / exit 0: verify-no-changes | `pr11-closeout-observation-format.log` |

`git diff --check` passed at review and again on the final documentation. These
automated results do not certify live shell compatibility, accounting accuracy,
locked-session acceptance or unattended readiness. No live observation or native
switch was executed. Recommendation is **READY FOR REVIEW** for the bounded
policy correction with the owner
and presentation limits above explicitly retained. Merge/deploy/release remain
unauthorized.
