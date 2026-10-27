# Issue #9 — focused monitoring diagnostics and regressions

Base: refreshed `origin/main` `cb738d839defc653beb5288734ecaad7f78ca6f3`.
This change measures owner execution and checkpoint latency and corrects one
deterministically demonstrated heartbeat freshness defect. It does not establish
the cause of PR 4's historical 107 missed deadlines or 13.1% Unknown coverage.
See [PR 4 accounting](pr4-accounting.md), [closeout](pr4-closeout.md),
[PR 11 policy](pr11-personal-usage.md) and
[issue #9](https://github.com/NDePetris/offramp-shift/issues/9).

## Verified heartbeat defect and narrow correction

Before correction, a Tick received at monotonic 1,000 ms, queued until 4,000 ms,
renewed health: `Drain` assigned processing time to `lastTick`. A new host
regression failed against the unchanged implementation (300 passed / 1 failed).
This proves a deterministic safety defect, not its occurrence or contribution
to the historical native run.

The host now retains the Tick's original monotonic receipt and epoch. Health and
the reminder freshness budget use its age, allowing exactly 2,000 ms and rejecting
2,001 ms, future timestamps and different epochs. Power/provider resets clear the
heartbeat. A new receipt can restore health; accounting still needs usable fresh
endpoints. This does not claim that WM_TIMER receipt certifies historical pump or
event completeness. No new retry, watchdog, timer, switch or recovery is introduced.

## Aggregate diagnostic schema

Existing fields remain. All histograms use existing fixed-size microsecond buckets:
`count`, `totalUs`, `maximumUs`, `p50Us`, `p95Us`, `p99Us`. Percentiles are bucket
upper bounds capped by the observed maximum. No individual timings are retained.

The existing headless monitor exports the following under `diagnostics`:

| Field | Meaning |
|---|---|
| `execution.step` | Complete Runtime Step, including drains, finalization, Core Observe, Save and post-commit checks; records even exceptional exits |
| `execution.sampling` | Completed observation cycle, including provider replacement/read, resource sampling, drain and estimator Point |
| `execution.drain` | Complete signal-drain calls, including calls from commands |
| `execution.finalizationAndDelivery` | Finalize plus delivery of its batch, synchronous saves and reminder checks |
| `execution.observe` | Individual durable Observe attempts, including failed attempts |
| `periodicSchedulingDelay` | Periodic samples only; unlike retained `schedulingDelay`, not diluted by event-only zeros |
| `freshness.queueResidence` | Native receipt to host processing, only same-epoch nonfuture receipts |
| `freshness.endpointSpacing` | Consecutive same-epoch nondecreasing sampled endpoints; not independent coverage |
| `freshness.coreReceiptAge` | Observation endpoint age just before durable Observe; Core reads the clock again |
| `freshness.acknowledgmentAge` | Endpoint age after successful durable Observe, including no-ops; slow acknowledgment does not retroactively invalidate valid receipt |
| `freshness.invalidSignalReceipts` / `staleHeartbeats` | Invalid epoch/time receipts / ticks unable to establish fresh health |
| `providers.resourceSampling` | Resource collection after Sampler constructs its evidence point; included in Step/cycle, outside CollectionMs |

Each execution entry contains `latency` (histogram), `slowOperations` (wall time
strictly over 500 ms), and `deadlineCrossings`. The latter counts fixed one-second
sampling boundaries in `(operation entry, operation exit]` at or after the next
scheduled deadline known at entry. It excludes lateness already present at entry.
This is operation/deadline overlap, not proven cause or an alternative count of
`MissedDeadlines`. A boundary crossing alone need not produce a missed deadline.
Parent/child measurements overlap: never sum their durations or crossing counts.
Sampling timing describes completed cycles; complete Step also captures failures.

`MonitoringController.FinalDiagnostics` publishes one anonymous snapshot after a
graceful owner-loop stop, before cleanup. `owner.turns`, `owner.commands`, and
`owner.publication` have the same execution shape. Turns include the normal
10 ms command wait, command, Step and publication; commands include their immediate
publication. Startup/cleanup are excluded. `monitoring` contains the runtime schema
above. Completion/cleanup success remains separately determined by `Completion`.
Nothing polls or serializes these detailed aggregates on the live UI refresh path.
No diagnostic UI, file writer or automatic export is added. The headless monitor
uses Runtime directly and therefore does not report controller turns/commands.

Under `commits`, existing attempts/acknowledgments/failures/no-ops and whole-Save
latency remain. `phases` adds:

| Histogram | Scope |
|---|---|
| `steadyState` | Established-store Save path from marker check through acknowledgment; includes phase gaps and fault seams |
| `connectionSetup` | Physical unpooled open, metadata/journal validation, EXTRA setting and verification |
| `transactionBegin` | Nondeferred transaction acquisition |
| `existingStateValidation` | Metadata, integrity/schema/row/digest, revision/payload conflict checks and required stored-payload decode |
| `replacement` | Replacement command/digest/parameters/update |
| `commit` | Transaction Commit, including exact acknowledgment-loss retry's read transaction |
| `disposal` | Separate transaction and connection disposal samples; normally two per successful established Save |

Initialization/reinitialization are excluded from phase histograms. Established
migration/retry saves are included; isolate fresh-store steady-state runs from
those paths when comparing. Whole-Save latency additionally includes encoding,
recovery preflight and initialization when applicable. Phase samples record even
throwing operations; failures before a phase have no sample for it. Open failure
cleanup belongs to setup. Fault hooks outside a phase remain visible in the full
Save/steady-state measurements. Instrumentation does not change journal mode,
EXTRA, integrity checks, timeouts, connection lifetime, transaction ordering,
frequency, pending payload identity or the disposal-before-acknowledgment boundary.

`coverage.unknown` is a fixed set of `{ count, durationMs }` entries:
`EndpointDiscontinuity`, `ObservedTransition`, `UnavailableEndpoint`,
`SignalInvalidation`, `PendingOverflow`, `StaleGap`, and
`FinalizationHealthOrFreshness`. Each finalized Unknown contributes once to the
category of its last invalidation stage. These are aggregate pipeline categories,
not exclusive physical root causes. Their durations sum to estimator `UnknownMs`,
not necessarily Core-accepted Unknown. `coverage.rejectedDurationMs` counts rejected
delivery duration; `discardedPendingIntervals` counts entries cleared by estimator
Reset, including entries replaced by a delivered stale-gap collapse. Counts are
not unique elapsed coverage and must not be added to Unknown duration.

Use existing `accountingDelivery` for Core acceptance/rejection. Startup, pauses,
resets, failed/discarded batches and final pending tails prevent treating
estimated-plus-Unknown as a complete elapsed-time ledger. Unknown scope is never
silently allocated to Personal/Work. State-specific cycle costs are not independent
state-specific coverage measurements.

Aggregates contain no application identities, paths, HWNDs, PIDs, role GUIDs,
titles, domains, URLs or activity sequence. Counters are process-local and bounded
in storage size. Provider replacement still resets provider-local histograms.
No telemetry, network service or automatic diagnostic persistence is added.

## Deterministic regression evidence

Host tests use fake observation/monotonic clocks and real isolated temporary SQLite
stores. Controller tests drive an internal pre-turn boundary on the owner thread;
production supplies no callback. Slow Save seams advance synthetic time rather
than sleeping or claiming a storage benchmark. Normal command wait/scheduling and
native-provider activation are unchanged. Tests cover:

- Delayed heartbeat, exact age boundary, future/epoch rejection, power clearing,
  stale queued heartbeat during Save and fresh receipt/accounting resumption.
- Full controller/application/runtime/durable-store path with one successful Save
  delayed by 499/500/501/1,000/1,001/3,001 ms. Missed deadlines, operation overlap,
  1,500 ms endpoint protection, stale-gap collapse, fresh-estimate resumption,
  unchanged saved usage across restart, and no spurious break/effects are checked.
- Application-owned multi-interval delivery: a first Save ages the next observation
  to exactly 2,000 ms (accepted) or 2,001 ms (rejected). Remaining stale batch is
  discarded and fresh Work estimates resume.
- Phase sample accounting, anonymous exports and Unknown-duration reconciliation.
  Existing failure/exact retry, lost acknowledgment, process-termination,
  uncertainty, pause/power and presentation safeguards remain required.

All seven required checks passed with pinned SDK 10.0.401 and warnings as errors:

| Command | Automated result |
|---|---|
| `./run.ps1 test` | Core 82 / 40,598 assertions; Persistence 84 / 778; Host 316 / 3,155; App 117 / 195; zero failures |
| `./run.ps1 format` | PASS, verify-no-changes |
| `./spikes/VirtualDesktop/run.ps1 test` | 58 fake-provider cases plus three owned non-COM isolation checks |
| `./spikes/BrowserAttribution/run.ps1 test` | 2,053 policy/protocol assertions, five mock extension tests and synthetic transport/process checks |
| `./spikes/WindowsObservation/run.ps1 test` | 41 tests / 10,300 assertions, zero failures |
| `./spikes/WindowsObservation/compatibility.ps1 test` | 24 unsupported-entry cases, pure presentation decisions and 16 diagnostic groups, zero failures |
| `./spikes/WindowsObservation/run.ps1 format` | PASS, verify-no-changes |

Root checks used process-local `UseArtifactsOutput=true` and
`ArtifactsPath=<repo>/artifacts/issue9-build`; probes used their established output
paths. Logs are ignored local `artifacts/issue9-*.log`. `git diff --check` passed.
The initial sandbox build stopped without compiler diagnostics; automated checks
were then run outside that build restriction. No active owner app was stopped or
reconfigured. Synthetic clock advances establish correctness, not latency overhead
or current Windows scheduling/storage performance. The added timers/counters are
fixed-size and avoid per-sample history/allocation; their live overhead remains
unmeasured and must be considered in subsequent measurements.

No live observation, shell COM experiment, desktop switch, owner-data access or
long-running stability exercise was performed.

## Bounded owner-run Windows measurement — not run

Use only an already approved Windows build/x64 and ordinary unelevated PowerShell.
Record exact tested SHA/build, privilege, CPU/RAM, checkpoint storage device/type
and filesystem, power mode, monitor layout/DPI, Release binary and other workload.
Independently verify Personal/Work role GUIDs; keep them local. Do not reuse or
copy the active owner store. Build/test before the timed run, not concurrently.

For each independently timed five-minute segment, use a newly named store:

```powershell
$studyStore = Join-Path $env:TEMP ('OfframpShift-Issue9-' + [guid]::NewGuid())
./spikes/WindowsObservation/run.ps1 monitor 300 --desktop --personal $personal --work $work --store $studyStore
```

Run active Personal, inactive Personal, Work and locked segments separately;
reserve at most ten more minutes to repeat the worst segment under documented
ordinary load. Maximum initial study: thirty minutes. Current PR 11 policy counts
eligible inactive Personal usage; only Work/lock earns break credit. Compare an
independent stopwatch with committed totals; explicitly account for startup and
discarded final pending time. Return final anonymous aggregates and measured
differences, not activity histories. A final Ready state is not accuracy proof.

Compare Save phases/full latency, Step/delivery/Observe, deadline overlaps and
misses, queue residence, endpoint/receipt ages, Unknown categories, Core acceptance
and fresh recovery. The headless run isolates shared Runtime behavior; it does
not measure WPF/controller command contention. No controller live-validation
claim follows from its synthetic regressions. If that path needs investigation,
plan a separate bounded isolated-directory controller harness before running it.

Stop on persistence/cleanup failure, unexpected shell behavior or false charges/
resets. Preserve isolated data; do not corrupt storage, reset accounting or silently
retry. Do not exercise Explorer restart, sleep, reboot or switching in this study.
The deferred two-hour run and mature matrices are not reinstated by this procedure.

Automatic switching and production browser attribution remain NO-GO. PR 5 Tests
8–12 remain deferred/NOT RUN. Issue #8 recovery and issue #9 sustained accuracy/
reliability remain unresolved; unattended daily use is not approved. Any subsequent
optimization requires measured evidence and a separate focused review.
