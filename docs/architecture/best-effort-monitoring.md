# Best-effort monitoring (PR 4)

The approved product is voluntary advisory accounting. Certified intervals remain
distinct from estimates. Automatic desktop switching and browser website
attribution remain NO-GO. This work introduces no production shell, notifications,
activity history, telemetry, network behavior, licensing or distribution decision.

## Boundaries

`OfframpShift.WindowsObservation` extracts the spike's native loop, clock, identity,
last-input reconstruction and persistent read-only COM worker. The probe still
owns command dispatch and sampling. The worker launches the entry executable,
not the extracted library. Its internal manager declaration still ends before
switch slots, and activation remains opt-in and restricted to exactly
26200.9457/x64 and 26200.9711/x64. The latter has owner-reported bounded read-only
compatibility evidence and explicit owner-evaluation approval; see
[the evidence](../validation/windows-26200-9711.md). This approval does not establish
accounting accuracy, uninterrupted monitoring or unattended daily-use reliability.
The separate VirtualDesktop switching allowlist and NO-GO gate are unchanged.
The original uncertainty-only EvidenceInvestigation is retained unchanged in
meaning. Known generic process brokers have unresolved application identity.
Identity resolution corroborates process creation time through a second limited-query
handle to detect PID reuse. No identity cache is added without measured need.

`OfframpShift.ActivityMonitoring` provides a serialized deterministic estimator,
without OS calls, timers, threads or database dependency. Immutable native signals
invalidate pending intervals; matched snapshots can support Estimated, never
Confirmed. The probe's `monitor` command integrates it with DurableMonitoringSession.
Native callbacks only enqueue signals; they never resolve identity or mutate Core.

## Accounting and timing

Core defaults to CertifiedOnly. BestEffort must be selected explicitly. Enum values
for existing confidence/input variants remain unchanged; Estimated and QualifiedIdle
are appended. Estimated intervals are capped at 1,500 ms and delivered within
2,000 ms of their end, also respecting the configured Core receipt freshness.
Certified interval duration semantics are unchanged.

The host reconciles on fixed one-second monotonic deadlines, with event-triggered
requests coalesced for 50 ms and sampled no more often than every 100 ms during
bursts. Missed cycles are not replayed. Pending intervals wait 1,000 ms after
their endpoint before finalization; finalization runs independently of polling
tick jitter. The closing snapshot and current required provider state must still
be fresh. This is not a cross-provider historical coverage watermark.

If the oldest pending endpoint exceeds the stricter estimator/Core receipt-age
limit, pending history and the gap through the current receipt are delivered as
one invalidating Unknown interval ending now. No part earns credit. The pending
queue and snapshot baseline are discarded; two new usable endpoints are required
before estimates resume. This prevents stale delivery after suspend from advancing
Core's rejection boundary repeatedly ahead of held-back intervals. Core freshness
checks and synchronous persistence are unchanged.

The host acknowledges each successful durable Observe result back to the estimator.
A Core clock or interval rejection discards the remaining finalized batch and
queued history, requiring two new usable endpoints after the rejection receipt.
This also covers delays between finalization and Core receipt (including preceding
commit latency), which the estimator cannot anticipate. Existing Core result
reason strings identify these two rejection paths; integration regressions protect
that coupling without changing Core contracts. No rejected evidence is retried or
credited. Final accountingDelivery counters report Core-accepted estimated/unknown
durations, rejection count and last Core reason separately from estimator totals.
Unknown acceptance means handling without credit, not verification of the gap.

Foreground/session reads bracket desktop sampling. Batches longer than 500 ms
cannot support estimates. Gaps over 1,500 ms, changed generations/shell, loss,
power/clock discontinuities and contradictory transition evidence invalidate
continuity. Missing identity/capability without a material change can be transient.
Two usable endpoints are needed after recovery. A bounded pending queue collapses
overflow to unknown evidence, never to a larger estimated interval.

Personal requires an explicitly bound GUID, active session, available input,
stable verified application identity and classification. Work and lock require
known scope but not foreground application identity. Unknown scope earns nothing.
Work foreground changes and Work input resumption do not themselves end a break.
Observed desktop excursions, including change-and-return, invalidate affected
pending intervals. Undetected excursions remain possible estimation errors.

Idle qualification counts only observed healthy awake time since a fresh baseline
or valid latest input. Before the configured 60-second threshold, eligible
Personal time can count. At qualification, intervals split mathematically and
QualifiedIdle earns break credit directly. Certified Idle retains Core's original
qualification rules. Startup, retry and restart receive no historical idle credit.
Temporary gaps freeze qualification; regressing/ambiguous input ticks invalidate it.
Already-idle startup time is not charged as Personal while fresh qualification
is accumulated; it is reported unknown and earns no historical break credit.

Core preserves an incomplete break through at most 5,000 ms consecutive and
15,000 ms cumulative transient uncertainty. Exactly equal boundaries are allowed;
one additional millisecond invalidates progress. Healthy observations clear only
the consecutive counter. Unknown time advances no usage, break or intervention
timer. Confirmed nonqualifying activity and material discontinuities invalidate
incomplete progress. Best-effort uncertainty alone does not rearm completed resets.

Late contradictions cannot rewrite committed estimates: they invalidate pending
evidence and subsequent continuity and are counted. Valid durable state can still
contain an inaccurate estimate. Live acceptance must therefore assess false
charges, missed charges, false exhaustion and false break completion independently.

## Durability and migration

Version 2 adds accounting mode/policy version, estimated usage portion, and the
two uncertainty counters. Configuration and each version's JSON field set are
strictly checked. Version 1 is readable only with the exact original field set;
it is interpreted as certified-only with zero new counters. Encoding writes
only version 2. Unknown future domain versions remain incompatible, including
recovery-copy inspection. SQLite schema/user_version stays 1 independently.

Opening a valid version-1 store restores its usage, pause, exhaustion, configuration,
effect sequence, intervention timers and completed latch, discards partial
continuity, then atomically replaces the payload at the next store revision.
Ready is published only after that migration commit. Migration failure retains
the exact pending checkpoint for explicit retry; acknowledgment loss does not
cause a second revision increment. Invalid/future metadata and malformed payloads
do not migrate. An old binary will reject the new domain version rather than
silently downgrade it.

The host deliberately selects BestEffort via a committed configuration change
after restoring an older certified-only configuration. Totals are never reset
by that selection; an accounting-mode-only change retains the completed-break
suppression latch, preventing an extra reset after migration. Other configuration
changes retain their original continuity rules. Totals are never reset
by observation-provider failure. Pending persistence blocks accounting and state
publication; R retries the exact commit. O explicitly recreates read-only providers
with a ten-second cooldown. Explorer generation changes permit one replacement
helper attempt per new shell generation after that cooldown. No switch retries
or destructive recovery are exposed.

Every durable change still commits immediately using PR 2 integrity checks,
rollback journaling and synchronous EXTRA. Genuine no-ops do not write. No batching
or crash-loss window is introduced. Effects are exposed only after commit and
counted by the headless host; no notification sink exists. Current context can
suppress obsolete advisory effects. Restart/retry does not replay effects or downtime.

## Aggregate instrumentation and limitations

Final foreground diagnostics count anonymous failed-read categories and show the
last category plus a numeric application-identity API return code when available.
No identity, window/process identifier, path, title, AUMID or event history is
included. Diagnostics do not relax generic-host exclusions, corroboration checks
or query permissions, and cannot locate individual failures within an interval.

Fixed-size logarithmic microsecond histograms report upper-bound p50/p95/p99,
counts, totals and maxima, without retaining samples. The host measures provider
queries, IPC, cycles, scheduling delay, queue depth/loss, state-specific cycle
costs and resources. The durable session measures Save attempts, acknowledgments,
failures/no-ops and full Save latency, including integrity/transaction/disposal.
Acknowledgments are not a claim that every attempt caused a distinct transaction.

Owned-process CPU is normalized by elapsed time and logical processor count.
Resources are sampled every ten seconds; peaks between samples may be missed.
Host process write-transfer bytes include non-disk IO and do not measure physical
SSD write amplification. No raw activity diagnostics are persisted automatically.
Helper/provider latency histories restart when those providers are replaced;
resource totals retain observed retired helper CPU where available.

The helper has its existing two-second failure deadline. In-process documented
native calls cannot be forcibly canceled safely. A blocked call cannot continue
charging usage, but could delay reporting/recovery. These remaining OS-call,
hosted-app, silent stale-provider and event-completeness limits require live review.

See [Stage 2 validation](../validation/pr4-accounting.md). No MVP readiness claim
is justified by synthetic tests or the Stage 1 focus results alone.
