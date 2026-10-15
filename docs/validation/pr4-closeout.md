# PR 4 bounded investigation closeout — October 14, 2026

The investigation is closed with partial live acceptance and explicit limitations.
No further operator exercises are requested for this closeout. The owner declined
the new two-hour resource exercise; it is NOT RUN, not passed or replaced by an
older measurement. The branch is `feat/best-effort-activity-monitoring`, based on
`55ebea8079b5492faca0927bc3f6b9359e34464a`. After reviewing the closeout, the owner
authorized committing the validated changes, pushing this branch, opening an
experimental PR and verifying GitHub CI. Merge, deployment and release remain
unauthorized; publishing the PR is not full accounting or production acceptance.

## Recommendation

**GO for review of this bounded diagnostic/best-effort experiment with these
limitations. NO-GO for full PR 4 acceptance, production readiness or certified
live accounting.** Functional diagnostics and selected accounting behaviors are
demonstrated; precise independent accuracy, complete event delivery, sustained
resource behavior and the mature transition matrix are not established.

Automatic Windows desktop switching and production browser-to-window website
attribution remain **NO-GO**. No production UI, notification delivery, desktop
switching, telemetry or browser attribution was introduced. The read-only desktop
COM adapter remains isolated and gated to 26200.9457/x64; no allowlist expansion.

## Reviewed implementation

- Native observation is extracted into WindowsObservation; deterministic estimation
  is in ActivityMonitoring. The existing probe provides the explicitly invoked
  headless monitor. No new production host or service is added.
- Core defaults to CertifiedOnly. Approved BestEffort opt-in uses Estimated,
  bounded freshness and explicit uncertainty, never historical Confirmed evidence.
  Healthy hooks or matching snapshots do not establish certification.
- Eligible Personal activity charges usage; Work and qualifying idle/locked time
  earn break progress. A completed fifteen-minute break resets once. Material
  uncertainty clears partial continuity; bounded soft uncertainty earns nothing.
- Version-2 payloads retain estimated usage/uncertainty and strict version-1
  migration. Immediate synchronous SQLite checkpoints precede effects. Restart
  retains saved totals, discards partial continuity and earns no downtime.
- Suspend/receipt rejection recovery discards stale batches and requires fresh
  observations without weakening Core freshness or durability. The host explicitly
  handles both interval rejection and power-related clock-epoch rejection.
- Final aggregate delivery and foreground-failure diagnostics distinguish Core
  acceptance from estimator totals without retaining app/content/event histories.
  Core result reason strings are a tested coupling in delivery recovery.
- One automatic read-only helper replacement per shell generation may initialize
  too early; the selected automatic Explorer recovery failed. Explicit O retry
  after settling succeeded. Extra automatic retries or silent rebinding were not
  added. Generic hosted foreground identities remain deliberately unsupported.

## Evidence assessment

Live results below are owner-reported on Windows 26200.9457/x64, two monitors;
the earlier long measurement was agent-executed. Exact privilege/DPI/power details
were not supplied for every owner exercise. They do not establish other host support.
See [the full evidence](pr4-accounting.md) for aggregates, timings and earlier failures.

| Check | Assessment | Limit |
|---|---|---|
| Original three-minute Personal/Work exercises | Functional smoke | Host timing only; never independently verified accuracy |
| Ninety-second idle | Functional, independent stopwatch reference | 58.710 s usage / 30.989 s break; selected run only |
| Eighteen-minute break exercise | Functional completion and one reset | Prior unsuccessful run remains unexplained; reset instant unmeasured |
| Restart retention | Functional pass | Exactly 176.874 s retained; approximate independent run timings; downtime unmeasured |
| Work lock/unlock | Functional pass | Locked cycles observed, conservative break invalidation; no exact locked-time credit |
| Sleep/resume | Functional pass after corrections | Two preceding failures retained; latest final streak 118.823 s, usage preserved; no historical certification |
| Explorer automatic recovery | FAIL | Desktop/bindings remained unavailable after single attempt |
| Explorer explicit O retry | Functional pass | Settled-shell fallback demonstrated, not automatic recovery reliability |
| Selected generic-host app | Unsupported identity demonstrated | Sixty GenericHostUnsupported reads; Personal coverage remains Unknown |
| Different observable app | Functional foreground pass | 56.987 s credited in host's minute; independent duration not supplied |
| Adjusted pinned/keyboard transitions | Functional transition smoke | Work-first schedule honored; 138.026 s Personal versus nominal 145 s, with 7.580 s Unknown; exact completed-switch/foreground continuity not established |
| Earlier two-hour run | Partial resource evidence | CPU/sampled memory below initial targets; 107 missed deadlines, about 13.1% Unknown; predates final corrections |
| New two-hour active/Work/locked resource run | NOT RUN — declined | No current sustained-growth, awake-tail or disk-specific acceptance |
| 200 foreground / 200 desktop transition matrix | NOT RUN | Stage 1's twenty-transition checks and short smokes do not substitute |
| Gaming/fullscreen, broader DPI/hardware, battery/energy | NOT RUN | No compatibility/impact claims |

Precise accounting error thresholds have not been accepted against independently
measured eligible segments. Unknown totals are not allocatable per state from
the final aggregate. Rejected/discarded delivery can leave elapsed time outside
reported coverage. Suspend-spanning commit/deadline timing is not awake disk or
scheduler latency. Resource peaks cannot establish absence of sustained growth.

## Final automated validation

| Command | Result and evidence boundary |
|---|---|
| `./run.ps1 test` | PASS: Core 67 tests / 38,679 assertions; Persistence 65 tests / 618 assertions, including five owned actual Windows forced-termination scenarios |
| `./spikes/WindowsObservation/run.ps1 test` | PASS: 41 tests / 10,237 assertions; synthetic observation and real local SQLite integration; no native observation API activation |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS: 58 fake-provider tests and three owned worker-isolation checks; no live switching/COM |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS: 2,053 synthetic assertions, five mocked extension groups, registration/syntax/native synthetic transport; no real browser validation |

Root and WindowsObservation formatting verification PASS. All validation-command
Release builds completed with zero warnings/errors. Diff checks, changed/untracked
source whitespace, project XML, lock-file JSON, modified PowerShell syntax and
closeout documentation links PASS. The source review covered approved Core mode
and checkpoint changes, strict migration/retry, estimator boundaries, serialized
host delivery, read-only native isolation and aggregate instrumentation. It added
a deterministic power-epoch recovery regression, with no further behavior change.
Existing switching/browser spikes and CI workflow are unchanged. No live commands
were run during this closeout. Automated passes do not waive any live gate.

## Remaining decisions

Full acceptance would require separate authorization/evidence for precise accuracy,
the mature transition matrix and sustained resources. Automatic Explorer recovery
needs either a bounded timing correction with live validation or an explicit
product decision accepting manual retry. Supporting generic hosted apps would
require a separate privacy-preserving identity design; do not attribute brokers
or inspect titles/content as an implicit workaround. No such expansion is part
of this closeout, and no further manual testing is requested here.

See [architecture](../architecture/best-effort-monitoring.md) and
[Stage 1 scope/evidence](pr4-stage1.md). Existing licensing remains unchanged.
