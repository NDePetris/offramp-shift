# PR 5 validation — owner evaluation only

Base: `e73fe57` (merged PR 4), branch `feat/first-usable-windows-app`.
This record distinguishes automated work from live Windows acceptance. No actual
monitoring session, desktop switch, Explorer restart, lock or suspend exercise
was performed by the agent during implementation or closeout. Later owner-reported
live evidence is recorded separately below.

## Original automated results (before main integration)

Windows 11 25H2 **26200.9711/x64**, pinned .NET SDK 10.0.401, Release,
warnings as errors. At that validation the PR #11 branch retained the 26200.9457/x64
observation allowlist. Native monitoring on 26200.9711 was blocked in that revision;
the later owner smoke used a detached checkout with the separately approved
PR #13 predicate. The automated results below do not expand the gate.

| Check | Result | Evidence boundary |
|---|---|---|
| Core | PASS: 67 tests / 38,679 assertions | Synthetic domain observations |
| Persistence | PASS: 65 tests / 618 assertions | Real local SQLite, injected faults; five owned Windows forced-termination scenarios reported separately |
| Application host | PASS: 25 tests / 103 assertions | Fake providers/clock, real temporary SQLite and settings; no native observation |
| WPF application | PASS: 9 tests / 70 assertions | Synthetic states/theme/high-contrast artwork selection, packaged canonical resources, actual WPF layout/automation properties, current-user activation process, actual WinExe non-COM worker fault modes |
| Windows observation | PASS: 41 tests / 10,237 assertions | Existing synthetic estimator and real local SQLite integration |
| Virtual desktop probe | PASS: 58 tests + three isolation checks | Fake providers and owned worker faults; no shell COM/switching |
| Browser probe | PASS: 2,053 assertions + five mock groups + transport checks | No Chrome/Edge interaction or attribution validation |

Reproduction: `./run.ps1 test`, `./spikes/WindowsObservation/run.ps1 test`,
`./spikes/VirtualDesktop/run.ps1 test`, `./spikes/BrowserAttribution/run.ps1 test`.
Root/observation format verification and final diff checks PASS. The UI skill's
mechanical detector returned no findings (not a native accessibility certification).
CI retains all prior jobs; the root Windows job now
also builds/tests the host and WPF app. No interactive COM tests run in CI.

Synthetic light/dark Home/Settings renders were inspected locally under ignored
`.tools/pr5-ui`. They exposed and verified correction of a read-only progress
binding and Fluent brush/style inheritance. These are render checks, not real
desktop, tray interaction, keyboard, Narrator or high-contrast acceptance.

## Owner-reported bounded smoke — closeout October 15, 2026

Windows 11 25H2 Professional **26200.9711/x64**; two side-by-side monitors,
**2560x1440 at 150%** and **1920x1080 at 100%**. The owner reported the integrated
WPF smoke successful using detached PR #11 source
`7026b55517c36cd73fc91fce093cc6c9011a7586` plus the approved compatibility predicate.
This is a combined working tree, not a single tested PR #11 commit. The exact
overlay commit/digest and a separate full runtime/hardware/privilege transcript
were not returned. No live monitoring or cleanup was independently run by the
agent. Procedure and diagnostic provenance are in PR #13's
[compatibility record](windows-26200-9711.md).

| Check | Owner-reported result | Limit |
|---|---|---|
| Main setup, Personal/Work observations | PASS, bounded smoke | No separate distinct-ID rejection, role restoration or topology-mutation acceptance supplied |
| Dashboard, tray and application interactions | PASS, bounded smoke | Does not establish every lifecycle, fault or accessibility case below |
| Pause/Resume | PASS, bounded smoke | Durable Pause across restart and no downtime credit not separately reported |
| Home visual review | Reviewed by owner | No separate Narrator, keyboard, high-contrast or per-monitor scaling acceptance supplied |
| Production observation probe | PASS, bounded smoke | Desktop Available (2); foreground/input/hooks/session/power Available; pump Responsive; 10 samples, 0 drops/callback errors; accounting Gated, 0 certified ms, MatchingEndpointsAreNotCoverage; exit 0 |
| Probe/app/owned-worker cleanup after the later smokes | NOT REPORTED | Diagnostic cleanup is separate evidence and cannot certify these runs |

The owner observed passive video playback without keyboard/mouse input eventually
earning break credit on Personal. This is known current idle-based product-policy
behavior, not evidence of observer or PR 5 implementation failure. Changing break
eligibility needs a new product-policy decision and separate work; this closeout
does not alter accounting or add media/browser observation.

## Remaining original manual acceptance — not established by the report

The broad checklist items below remain unchecked when only part was covered by
the bounded smoke above. Unreported steps are pending, not failures. Do not infer
cleanup, persistence, accessibility or fault-path passes from ordinary interaction.

For each exercise record exact app revision, Windows build/UBR and x64, runtime,
privileges, CPU/RAM, storage/filesystem, power mode and monitor layout/DPI. Use an
isolated local store for destructive/fault exercises; preserve ordinary data.
Perform only deliberate owner-led exercises on the allowlisted configuration.

- [ ] Launch with `./run.ps1 app`; consent, tray-based Personal/Work assignment,
  distinct-ID rejection and explicit first Start all work without console parsing.
- [ ] Restart retains settings/totals, resumes enabled unpaused monitoring only
  after fresh observation, and preserves Pause without downtime credit.
- [ ] Independently check Personal/Work/Other/Unknown, usage/break progress,
  completed reset, exhausted/grace/nudging and unavailable feedback. Unknown time
  never looks earned. No tray balloon/toast, switching or window movement occurs.
- [ ] Duration edits save before success; failed writes freeze accounting.
  Retry save retries the exact pending commit. Incompatible and clock-blocked data
  never offer reset. Deliberate recovery preserves originals before replacement.
- [ ] Reorder/rename desktops preserves GUID roles; deletion/recreation requires
  explicit reassignment. No index substitution. Unsupported builds show unavailable
  without undocumented COM activation.
- [ ] Explorer restart reports unavailable where recovery fails; explicit Retry
  monitoring honors cooldown and fresh baselines. Check owned helpers after retries.
  This is manual fallback validation, not closure of issue #8.
- [ ] Lock/unlock and suspend/resume retain totals but credit no uncertain gaps.
- [ ] Close-to-tray, tray reopen, second launch, pending-save Exit, normal Exit and
  Windows logout are reviewed; check process/handle cleanup. Actual crashes do not
  replay effects or downtime. Delayed native shutdown is reported honestly.
- [ ] Keyboard/tab/access keys, tray keyboard access, Narrator announcements,
  light/dark/system/high contrast and enlarged text work. Test narrow window sizes,
  100/150/200% scaling and mixed-DPI monitors without clipped controls.

## Canonical assets and remaining gates

The owner subsequently supplied `offramp-design-v0.1.zip`. Eight canonical source/
runtime files were copied byte-for-byte with archive/entry SHA-256 provenance in
`src/OfframpShift.App/Assets/Offramp/provenance.json`. Geometry and pixel values were
not altered, archive scripts were not executed, and proposed semantic mappings
were not promoted. The executable/window/tray use the family ICOs provisionally;
a differentiated Shift-specific icon remains pending. Light/dark header render
checks and synthetic high-contrast decorative-art exclusion pass. Actual shell
icon appearance beyond the reported Home/tray review, high contrast and detailed
DPI acceptance remain unreported.

Both supplied ICOs retain all seven 16/24/32/48/64/128/256 frames, verified with
WPF decoding. Drawing.Icon decodes the tray sizes through 128px correctly here;
its chooser returns 128px for a 256px request. The tray requests 32px. No artwork
was changed to accommodate that decoder behavior.

Issue #8 (Explorer recovery) and #9 (independent accuracy/sustained reliability)
remain OPEN. No new two-hour measurement or mature transition matrix was run.
All prior automatic-switching/browser-attribution gates remain NO-GO. This PR does
not establish certified accounting, unattended daily use or production readiness.

## Original closeout verdict (before main integration)

The first usable owner-evaluation path is demonstrated by the reported integrated
smoke. The original issue #10 acceptance checklist is **not fully closed**:
Exit/owned-helper cleanup, restart restoring assignments/totals/Pause, lifecycle,
keyboard/Narrator/high-contrast/scaling, lock/suspend, provider Retry after Explorer
failure and storage-fault/recovery UI acceptance remain unconfirmed. Retain draft
status pending that acceptance or an explicit owner decision narrowing the
milestone's manual acceptance. These are actual missing original checks; revised
accounting policy, actionable interventions and switching are subsequent product
work and are not PR 5 feature blockers. Issues #8/#9 remain open independently.

Review covered the serialized host/shared runtime, consent and exact-build gates,
fresh GUID assignment, durable commands/settings, stale UI projection, worker
dispatch and shutdown reporting. No new blocking implementation defect was
identified in that review. The hard-coded 26200.9457 unsupported-build explanation
in `MonitoringApplication.Snapshot` must be reconciled when integrating #13;
this is an integration copy correction, not permission to broaden support.
Original-head CI run 38677854810 passed all four jobs; automated assertions do not
complete the manual checks above.

Agent-executed closeout: `./run.ps1 test` PASS outside the constrained sandbox
(Core 67 / 38,679 assertions; Persistence 65 / 618, including five owned Windows
forced-termination scenarios; host 25 / 103; WPF 9 / 70). Release builds reported
zero warnings/errors. The first constrained attempt passed Core, then stopped at
the persistence build with `Build FAILED`, zero warnings/errors and no diagnostic;
the unchanged suite passed outside. Retain that tooling failure separately from
the successful rerun. `git diff --check` also passed. No live observation was run;
only this Markdown record changed and no new behavior tests were needed.

Recommend #13 before #11, with separate explicit owner merge authorization. After
#13 merges, merge updated `main` into this branch, preserving the WPF/runtime/asset
commits and both CI additions. Reconcile overlapping architecture/observation docs,
keep the two exact read-only builds and compatibility regressions, and update the
build explanation and architecture documentation. Run root, observation,
compatibility, VirtualDesktop and BrowserAttribution tests plus formatting; verify
CI and repeat a deliberate WPF smoke on the actual integrated head. Avoid replacing
the branch with `main` or copying only the predicate and losing its tests/evidence.
No integration, merge, deployment, release or license change was performed here.

## Main integration — October 15, 2026

PR #13 was verified merged into `main` as
`22e6e65432c704bc80a8b4d2558b6081ec6e3b01`. That main revision is merged into
the existing PR #11 branch with both parents retained; the WPF application,
shared host, canonical assets and original branch history are preserved.
Architecture/observation README and CI edits merged without conflicts. CI retains
the Windows-app root tests and the compatibility guard/non-COM fault step.
Both exact read-only entries and all compatibility tests are retained unchanged.
The unsupported-build explanation now describes the exact-build/x64 requirement
without an obsolete one-build list. No policy, ABI, switching, accounting,
intervention or licensing behavior changed beyond integrating approved #13.

The older sections above record historical validation, not acceptance of this
integrated head. The owner confirmed no additional original acceptance checks
had been completed. Use [the prioritized acceptance checklist](pr5-owner-acceptance.md)
on the integrated head; all its live checks remain NOT RUN for that head.
Issues #8/#9 remain open; automatic switching, production website attribution
and unattended daily use remain NO-GO. PR #11 remains draft.

### Agent-executed integration validation

Pinned SDK 10.0.401; Release builds with warnings as errors, zero warnings/errors.
Checks ran outside the constrained sandbox because these same build/formatter
tools required it in the earlier closeout. No live observation or switching ran.

| Command/check | Result | Boundary |
|---|---|---|
| `./run.ps1 test` | PASS: Core 67 / 38,679 assertions; Persistence 65 / 618; WPF 9 / 70; initial host 25 / 103 | Synthetic observations, real temporary SQLite and five owned forced-termination cases; WPF layout/resources/activation and non-COM WinExe faults |
| Final host Release regression run | PASS: 26 tests / 112 assertions | Added integrated-policy test: both approved builds reach fake-provider accounting; 26200.9712 creates no provider; saved old-build settings on the new build require setup and earn no time |
| `./spikes/WindowsObservation/run.ps1 test` | PASS: 41 tests / 10,254 assertions | Synthetic evidence and real local SQLite; includes both exact builds and neighbor/architecture rejection |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS: 14 groups, seven owned non-COM child exercises; 12 unsupported constructor/worker-entry cases | Actual worker source with test-only registry/runtime/native stubs; no live COM |
| Observation `isolation-test` | PASS: hang/malformed owned workers | No retry or shell activation |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS: 58 fake-provider tests + three isolation checks | Switching policy and implementation unchanged |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS: 2,053 assertions, five mocked groups, synthetic transport | No Chrome/Edge attribution acceptance |
| Root and observation `format` | PASS | Verify-no-changes; includes host/WPF and retained probe |
| Diagnostic and guard-test `dotnet format --verify-no-changes --no-restore` | PASS | Both projects, no source rewritten |
| Diff whitespace and local documentation links | PASS | No conflict markers or missing local Markdown link targets in edited documentation |

The root run preceded addition of the new host regression; the separate final
host run validates that addition. The original assets, switching code and approved
read-only predicate/guard tests were compared directly with their branch/main
inputs: unchanged. No integration regression identified by these checks. The
integrated commit and final PR CI status are reported on PR #11; a successful
CI run is automated evidence only. No live checklist row is promoted to PASS.
