# PR 4 accounting and resource validation

Status: Stage 1 accepted by owner; Stage 2 implemented with automated regressions
passing. The two-hour native measurement completed; full performance acceptance,
independent Personal/Work accuracy and reset acceptance remain pending. Do not infer
their pass from synthetic regressions or the aggregate measurement.

Update: the owner supplied independently verified role GUIDs locally. An agent-run
90-second valid-binding smoke completed; independently timed accuracy acceptance
is still pending. The approved two-hour background run completed October 13,
2026, from approximately 20:23:14 to 22:23:14 UTC (15:23 to 17:23 America/Chicago).
Its final aggregate was read once after completion from ignored
`.tools/pr4-stability/summary.json`; no model-driven polling occurred during the run.
Do not infer accounting acceptance from Stage 1 or the following invalid-binding smoke.

## Native invalid-binding smoke (agent-executed)

Windows build 26200.9457/x64, two monitors, ordinary unelevated command, Release.
20-second run used deliberately nonexistent role GUIDs and an isolated ignored
`.tools/pr4-invalid-bindings-smoke` store. Real Windows observation returned
capabilities, but bindings were unavailable. Usage and break progress stayed zero,
effects stayed zero, and only initialization wrote a checkpoint. 18 no-op
transitions were reported. No desktops were switched or assigned by the agent.

Measured at the pre-finalization-scheduling-correction working tree:

| Metric | Observation | Limit |
|---|---|---|
| Duration/samples | 20,048 ms / 20 | Short invalid-binding run |
| CPU | 0.482% of machine; 1.547 CPU seconds; 16 logical processors | Includes startup; not an extended active result |
| Combined sampled peak working/private | 84,430,848 / 23,597,056 bytes | Not a two-hour memory/leak pass |
| Cycle p50/p95/p99 | 32.768 / 65.536 / 230.043 ms | Histogram bucket upper bounds |
| Desktop IPC p50/p95/p99 | 4.096 / 4.096 / 174.197 ms | Startup outlier included |
| Foreground identity p50/p95/p99 | 0.256 / 0.256 / 3.682 ms | No speculative cache |
| Session query p50/p95/p99 | 16.384 / 32.768 / 35.135 ms | Bracketing reads; 40 samples |
| Initial Save latency | 365.960 ms | No active per-second commit load measured |
| Missed deadlines/loss/errors | 0 / 0 / 0 | Selected short exercise only |
| Approximate host write transfers | 9,052 bytes | Includes non-disk IO; not SSD writes |

Hardware CPU model/RAM, disk type, monitor layout/DPI and power mode were not
recorded for this smoke. Later changes require remeasurement. Battery, gaming,
locked/sleep recovery and two-hour stability remain untested.

## Valid-binding native accounting smoke (agent-executed)

Owner-confirmed roles, exact build 26200.9457/x64, two monitors, isolated ignored
`.tools/pr4-confirmed-bindings-smoke` store. Duration 90,026 ms, 89 reconciliation
samples: 84,006 ms estimated Personal usage, 4,542 ms unknown, zero break progress
and effects. Persistence ended Ready; no drops/callback errors or late contradictions.
These are real accounting totals, not independently certified expected activity.

85 Save attempts/acknowledgments, zero failures, three no-ops, 50,712 prepared
payload bytes. Save p50/p95/p99 was 131.072 / 524.288 / 2,656.061 ms; one missed
sampling deadline and one invalidated interval. Synchronous storage is a measured
bottleneck under the concurrent SDK formatting/regression workload. Stale gaps
earned nothing; durability was not weakened. Disk hardware has not been identified.

Combined CPU: 0.394% of machine (5.672 CPU seconds, 16 logical processors).
Sampled peak working/private bytes: 98,066,432 / 33,193,984. Approximate host write
transfers: 1,431,082 bytes, including non-disk IO. This short run does not establish
the two-hour CPU target, lack of leaks, true disk writes or practical error rates.
The run predates the final ambiguous-event/future-policy guard tests; those guards
do not justify reusing this sample as a final acceptance measurement.

## Two-hour native aggregate measurement (agent-executed)

Completed 7,200,028 ms with 7,142 observation cycles on Windows 11 build
26200.9457/x64, two monitors, Release, owner-verified role bindings and an isolated
ignored checkpoint store. CPU: AMD Ryzen 7 2700X, 16 logical processors; RAM:
34,287,742,976 bytes. Storage for the checkpoint: G: NTFS on a SATA
ST2000DM008-2FR102 HDD, identified after completion. This is not an SSD write-load
measurement. Monitor layout/DPI, power mode and token elevation were not recorded.
The host was started hidden from the agent's approved execution environment.

Revision: base `55ebea8079b5492faca0927bc3f6b9359e34464a` on
`feat/best-effort-activity-monitoring`, with uncommitted Stage 1/2 changes. The run
binary predates the accounting-mode-only completed-break-latch correction. That
correction affects configuration changes after migration, a path not exercised by
this fresh BestEffort store. No raw event history or desktop GUIDs are published.

| Metric | Completed measurement | Assessment |
|---|---|---|
| Combined average CPU | 0.2064%; 237.766 CPU seconds | Under initial 0.5% target |
| Combined sampled peak working/private | 105,725,952 / 39,813,120 bytes | Under initial 150 MB target |
| Initial private / initial and maximum handles / maximum threads | 21,106,688 bytes / 601 and 617 / 28 | Peaks alone cannot establish lack of sustained growth |
| Observation cycle p50/p95/p99/max | 32.768 / 65.536 / 65.536 / 177.390 ms | Zero observation cycles above 500 ms |
| Active / idle / locked cycle counts | 2,878 / 4,264 / 0 | Locked measurement NOT RUN; segment CPU not independently isolated |
| Scheduling delay p50/p95/p99/max | 8.192 / 16.384 / 2,097.152 / 3,300 ms | 107 missed one-second deadlines; tail jitter remains a concern |
| Desktop IPC p50/p95/p99/max | 2.048 / 4.096 / 4.096 / 141.122 ms | Persistent helper; zero explicit recoveries |
| Foreground identity p50/p95/p99/max | 0.128 / 0.256 / 0.512 / 3.417 ms | Measured small relative cost; no speculative cache added |
| Session reads p50/p95/p99/max | 16.384 / 16.384 / 32.768 / 74.258 ms | 14,284 bracketing reads |
| Input reads p50/p95/p99/max | 0.001 / 0.002 / 0.004 / 0.760 ms | 7,142 reads |
| Save attempts / acknowledgments / failures / no-ops | 4,195 / 4,195 / 0 / 2,965 | Ready at completion; strict durability retained |
| Save p50/p95/p99/max | 131.072 / 1,048.576 / 3,901.888 / 3,901.888 ms | Synchronous persistence is the largest measured blocking cost |
| Prepared payload / approximate host write transfers | 2,532,838 / 71,031,040 bytes | Transfer bytes include non-disk IO, not physical writes |
| Queue peak / final / drops / callback errors | 33 / 0 / 0 / 0 | Maximum observed event delivery age 16 ms |
| Estimated / unknown interval time | 6,255,483 / 942,350 ms | About 13.1% unknown; no independent expected-time reference |
| Invalidated intervals / late contradictions | 67 / 0 | Unknown causes cannot be apportioned from aggregates |

Latency percentiles use fixed log2 histogram bucket upper bounds capped by the
observed maximum; they are approximate, not exact quantiles. Estimated interval
time includes all eligible estimated classifications, not just Personal usage.
Save frequency averaged 0.583/s across the run, with no-op transitions avoiding
writes. Accounting state changes still require durable replacement before effects.
Do not introduce batching or a crash-loss window to reduce this measured cost.

Final state: zero remaining usage/estimated usage, completed 900,000 ms break latch,
four committed effects and one context-suppressed effect; all capabilities available,
no persistence failure and no provider recovery. These final totals do not reveal
the independently timed Personal/Work/idle sequence or certify reset accuracy.
No UI, notification or automatic switching was performed by the host. Slow commits
are consistent with deadline misses, but aggregates do not prove each miss's cause.
Unknown time may also include ordinary transitions and unavailable observations.

CPU and sampled memory targets pass for this configuration. Reliable freshness,
resource-leak absence, disk-specific volume, locked/sleep recovery, mature rapid
transition matrices, gaming impact and battery/energy acceptance are not established.
The owner will run independent accounting checks later. Overall acceptance and a
local implementation commit remain pending; no additional monitoring run was launched.

## Automated validation after measurement

Core: 67 tests / 38,679 assertions. Persistence: 65 tests / 618 assertions, including
version-1 atomic migration, failures before/after update/commit, acknowledged-save
restore failure and exact retry without duplicate replacement, future policy rejection,
and preservation of the completed latch when selecting BestEffort after migration.
WindowsObservation/estimator: 36 tests / 10,204 assertions, rerun against the latest
Core correction; synthetic observation evidence with real local SQLite integration.
Real Windows forced-termination persistence checks pass separately from injected
exceptions. VirtualDesktop: 58 simulated tests plus three isolation checks;
BrowserAttribution: 2,053 assertions plus five mock groups and syntax/transport
checks previously passed. These do not establish live switching or browser attribution.
Release builds completed with zero warnings/errors. Root and WindowsObservation
format verification and `git diff --check` passed after the final correction.

## Run locally with independently verified bindings

From an ordinary PowerShell terminal, use Task View to corroborate each role.
Obtain the `currentDesktop` GUID with `snapshot --desktop` on each desktop. Use
separate terminals or manually pin the test terminal so returning to it cannot
silently change the desktop being sampled. Do not infer a role from a name/index.

```powershell
Set-Location G:\Projects\offramp-shift
./spikes/WindowsObservation/run.ps1 test
$personal = '9738d496-00be-42d1-9092-c4f1365ad90a'
$work = 'de499e40-c0e5-44f7-b385-5ec9b555eef7'
$store = Join-Path $env:TEMP ('OfframpShift-PR4-' + [guid]::NewGuid())
./spikes/WindowsObservation/run.ps1 monitor 180 --desktop --personal $personal --work $work --store $store
```

Keep explicit role GUIDs local if preferred; the host never prints them. The
command selects best-effort monitoring and stores version-2 accounting checkpoints
in the deliberately selected directory. It does not write activity histories.
The default 30-minute allowance and 15-minute break remain in effect. Ctrl+C ends
the run. Final pending time is discarded (roughly the final one to two seconds);
startup requires fresh snapshots/heartbeat. Account for those edges explicitly.

Use an independent stopwatch and anonymous application labels; do not derive
expected totals from the monitor itself. Return final JSON and expected/measured
differences, not paths/titles/URLs or a raw event ledger.

## Staged acceptance exercises

1. **Personal active:** For three minutes, use a resolvable ordinary application
   on Personal, keeping input active. Compare independent eligible time to UsageMs
   and EstimatedUsageMs; explain startup/end losses and UnknownMs.
2. **Work:** Use a fresh store and Work for three minutes, including focus changes
   and input resumption after idle. Require zero Personal usage and qualifying
   break progress without spurious resets. Do not relabel UnknownMs as credit.
3. **Idle:** Fresh Personal baseline, cease input for at least 90 seconds. Require
   one sixty-second qualification, eligible Personal time before it, then idle
   credit; no doubled threshold. Report actual input times and unknown duration.
4. **Transitions:** Manually change Personal/Work/Personal, including a pinned
   foreground window and brief change-and-return. Compare independent totals;
   transition uncertainty earns nothing. Separate ordinary application changes
   within Work from desktop changes.
5. **Break completion:** Accumulate known Personal usage, then independently time
   fifteen qualifying Work/locked minutes in one run. Confirm one reset only;
   repeat qualifying time must not produce repeated resets. Known Personal
   interrupts an incomplete break. Unknown/suspend time does not complete it.
6. **Recovery/restart:** After saving other work, deliberately exercise Explorer
   restart, lock/unlock and sleep/resume separately. Require unknown boundaries,
   fresh baselines, no stale charging or inferred sleep credit, and retained usage.
   R retries pending persistence explicitly; O retries observation providers.
   Persistence failure injection is regression-tested; do not corrupt real storage.
7. **Mature transition matrices:** After basic accounting passes, record 200 real
   foreground and 200 manual desktop transitions per claimed configuration.
   Include pinned windows/apps, multiple monitors and fullscreen applications.
8. **Resources:** Run `monitor 7200` with the same arguments for at least two hours.
   Include normal active, idle and locked segments with independently recorded
   durations. Compare host/helper CPU, working/private memory, handles/threads,
   provider/commit p50/p95/p99, write transfers and missed deadlines. Use Windows
   resource tools for sustained-growth and disk-specific corroboration: the final
   aggregate alone cannot prove absence of leaks. Battery testing requires suitable
   hardware; otherwise mark it NOT RUN. Do not accelerate real time for this gate.

Record exact revision/working-tree state, Windows build/architecture, privileges,
CPU/RAM, storage type/filesystem, power mode, monitor layout/DPI, Release build and
duration. Targets are the approved plan: <0.5% combined average CPU, <150 MB combined
memory, no sustained resource growth, reliable bounded freshness, measured/justified
commit load, and the approved accounting/error/reset criteria. Never weaken
synchronous persistence to meet a target. Performance optimization requires an
observed bottleneck; batching and crash-loss windows require separate approval.

Ready-for-review recommendation remains pending independent accounting and the
remaining resource/recovery acceptance. No implementation commit is made before required validation
is resolved. No push, PR, merge, publication or release is authorized.
