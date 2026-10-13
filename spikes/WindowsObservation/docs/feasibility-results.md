# PR 3 feasibility checkpoint — October 13, 2026

**GO for retaining a bounded diagnostic investigation. NO-GO for live certified accounting
or an MVP-readiness claim.** Automatic virtual-desktop switching and production browser
website attribution remain NO-GO independently.

## Provenance and scope

PR 2 [#5](https://github.com/NDePetris/offramp-shift/pull/5) was verified merged.
Remote `main` was verified and fetched at `4cf5237dc94b9a865249c7754522a60ebe5c5b22`.
Branch: `feat/windows-observation-certification`. Original implementation/evidence
revision: `3901648`. The subsequent closeout authorizes validation, necessary corrections,
pushing this branch and opening a PR against `main`. Merge, deployment, publishing,
release and PR 4 implementation remain unauthorized.

The owner approved a staged investigation, with a feasibility stop before broader
integration. Added two projects under `spikes/WindowsObservation`, with a separate
read-only COM process justified by undocumented-interface isolation. Core, Persistence,
both existing spikes, their allowlists, licensing and CI remain unchanged. The new
probe never opens a durable session or submits live evidence to an accounting engine.
There is no production infrastructure, notification layer, telemetry or activity history.

## Independently checked environment

| Item | Observation | Limit |
|---|---|---|
| Edition / release | Registry EditionID Professional, DisplayVersion 25H2 | Legacy ProductName is not used as Windows marketing evidence |
| Windows build | 26200.9457 | Exact build/UBR independently reread |
| Architecture | x64 | Runtime independently reread |
| Monitors | 2 | Native GetSystemMetrics; layout, DPI and monitor models not verified |
| Desktops | 2 returned; ordinary current GUID present | No independent Task View corroboration or role binding in this run |
| SDK | Pinned repository-local .NET 10.0.401 | Release compilation and test runtime |
| Privileges | No elevation requested or implemented | Token privilege details not independently measured for this run |

CIM OS/monitor queries were unavailable in the sandbox; registry/runtime/native reads
provided the facts above. This does not establish other build or hardware compatibility.

## Targeted actual Windows evidence

| Exercise | Result | Evidence boundary |
|---|---|---|
| `snapshot --desktop` | PASS | Actual isolated COM current/topology read; foreground process key, last-input age, active session; hook, WTS and power registrations succeeded |
| `watch 12 --desktop` | PASS for reading current state | 11 observed samples, stable reported current GUID/topology and process key; 10,806 ms reported uncertain; zero certified time, dropped signals or callback errors |
| Last-input evolution in that run | OBSERVED | Ages grew from 1,375 to 4,578 ms, then fell to 594 ms; later newer input timestamps were observed. No physical-human-input or continuous-idle certification |
| `delivery-test` | PASS | Hidden owned window destroyed while our pump paused; native destroy callback received 2,515 ms after generation; zero local drops/callback errors |
| `foreground-test` | BLOCKED stimulus | Initial owned-window foreground request rejected. Initial attempt returned no detailed output; corrected diagnostics identified InitialForegroundStimulusRejected on one follow-up. No foreground A/B/A pass claimed |
| `isolation-test` | PASS | Owned helper timeout and malformed output fail explicitly, remain failed on another Read, terminate their owned process; no COM activation in either fault worker |

The initial watch printed sample lines during development. The final harness prints only
one current-state/aggregate summary, avoiding a raw observation stream; no raw capture
was written or committed. Final-revision smoke checks are recorded below.

The pump pause is an intentional local fault stimulus. Its measured 2,515 ms delivery age
is not a naturally observed worst case and does not establish a maximum OS latency.
Subscription success is not lock/unlock or sleep/resume validation. Current GUID returned
by the experimental API is not independent proof of the active desktop's role.

**NOT RUN:** operator-driven foreground/desktop change-and-return; pinned windows/apps;
Explorer restart; lock/unlock; sleep/hibernate/resume; idle threshold qualification;
desktop deletion/reordering; elevated/protected apps; fullscreen games; mixed DPI/hot-plug;
remote session/fast user switching; long-running resource and CPU profiling. No unrelated
process, shell, lock state, power state or desktop was manipulated to obtain evidence.

Full 200-transition matrices and durable-session/live usage/break acceptance were not run:
the strict certification feasibility gate was not met. Missing scenarios are untested,
not passes inferred from synthetic tests.

## Fundamental certification limitation

The actual API exercise demonstrates current-state queries and one delayed native event.
It supplies no complete, bounded-latency history combining foreground process identity,
active virtual desktop, session, power and input. Matching endpoints do not rule out an
intervening change-and-return; event receipt time does not establish transition occurrence.
Native generation ticks help diagnose delay but are not a cross-provider coverage watermark.
Last-input queries retain only the latest timestamp, and documented readings can regress.

Public IVirtualDesktopManager exposes window membership, not direct active GUID/history.
The isolated internal interface reads active GUID/topology, but its build-specific snapshots
cannot certify intervening desktop transitions. Pinned foreground windows make a foreground
change stream particularly insufficient as a substitute for desktop history.

A one-second reconciliation schedule and two-second local gap/worker deadline detect some
failures. They cannot establish a maximum missed-transition duration, a maximum lateness,
or a bound on cumulative error. Waiting two seconds before commit would only assume the
very guarantee that is missing. Hook installation and a pump heartbeat do not prove absence
of silent event loss. With Core's irreversible accepted accounting, later contradictory
evidence cannot correct a charge or false break reset.

Consequently **all live intervals remain Unknown/Unavailable**; current endpoint state is
kept separately. No live command constructs a Confirmed interval or opens an accounting
session. This is conservative diagnostic output, not successful certified monitoring.

## Options requiring a new decision

1. Retain strict certification and this investigation. Seek a documented coverage source
   or defensible constrained operating model before enabling accounting. Additional manual
   tests can expose failures, but empirical success alone does not establish completeness.
2. Separately approve best-effort observation semantics and corresponding Core/Persistence
   changes. Events plus reconciliation can plausibly provide useful estimates, but short
   missed changes, delayed transitions, pinned desktops, broker ambiguity, session races and
   latest-input-only readings can overcharge or incorrectly credit a reset. No numerical
   error bound has been established. A new confidence/policy contract must expose this;
   existing Confirmed must not silently acquire that meaning.
3. Consider explicit user-controlled context/timing instead of OS-certified application
   activity. This changes what the product measures and does not certify actual foreground
   or desktop behavior; it needs a product decision rather than an adapter workaround.

Recommendation: preserve the current strict gate and stop here for the owner's choice.
Do not complete durable integration or undertake the full acceptance matrix until the
evidence policy or product direction is defensible and explicitly approved.

## Reproduction of remaining targeted checks

Run `watch 30 --desktop` from an ordinary interactive console. Final output contains
current state and aggregate event/health counters only, never a per-event history.
Use operator notes and disposable test apps to independently corroborate endpoints:

- Manually focus A, then B, then A between reconciliation opportunities. Observe event
  counters; matching final identity remains uncertain regardless of delivered events.
- Manually switch Personal/Work/Personal with Task View, including a pinned test window.
  Supply deliberately verified role GUIDs if diagnostic scope labels are desired. A
  matching final GUID never certifies the intervening interval.
- After saving work, the operator may deliberately restart Explorer. The old helper
  generation must fail; restart the harness explicitly to obtain a fresh provider.
- Lock/unlock and, separately, suspend/resume. Record subscription/event availability,
  unknown boundaries and fresh epochs. There must be no inferred gap credit.
- Leave input quiet, then resume it. Latest-input age should grow/fall consistently where
  available, but it must not start Core idle qualification or earn break credit.

Record edition/build/architecture, privileges, monitor layout/DPI, exact commit, independent
operator observations, PASS/FAIL/NOT RUN and fault reasons. No test should change an existing
spike allowlist or exercise switching code. Full acceptance counts remain conditional on a
new certification decision, not a way to turn snapshots into proof.

## Automated regression and final checks

The new assertion executable invokes no Windows API. It covers equal healthy endpoints,
observed/missing change-and-return, unavailable identity/capabilities, changed shell/session,
signal duplication/reordering/loss/delay, time rollback/epochs, awake-clock discontinuities,
tick rollover/regression/ambiguity, exact build gates, generated interval invariants and Core
uncertainty integration. These tests verify refusal to invent evidence, not a positive certifier.

| Check | Result | Evidence type |
|---|---|---|
| New observation tests | 16 tests / 10,080 assertions PASS | Synthetic; no Windows APIs |
| Existing Core tests | 58 tests / 38,654 assertions PASS | Synthetic domain observations |
| Existing Persistence tests | 56 tests / 544 assertions PASS | Real local SQLite; injected failures and 5 owned Windows termination scenarios separately reported |
| VirtualDesktop `test` | 58 policy tests + 3 worker isolation checks PASS | Fakes and worker faults; no live switching |
| BrowserAttribution `test` | 2,053 policy/protocol assertions + 5 mocked extension groups, syntax/manifest/transport PASS | Synthetic caller; no real browser attribution |

Release builds had zero warnings/errors. Initial sandbox compilation failed without a compiler
diagnostic; the real outside-sandbox compiler then identified a nullable error, corrected before
successful tests. Both are development failures, not Windows observation compatibility claims.

Final investigation checks at implementation revision `3901648`:

| Check | Result | Boundary |
|---|---|---|
| New `test` after final code changes | 16 tests / 10,080 assertions PASS; Release zero warnings/errors | Synthetic refusal-to-certify and Core uncertainty integration |
| New `format` | PASS | SDK verify-no-changes, both new projects |
| Root `format` | PASS | Existing Core/Persistence projects; Release zero warnings/errors |
| Final `watch 8 --desktop` | PASS | One final JSON summary only; 8 samples, 7,650 ms uncertain, 0 certified; maximum last-input age 1,547 ms, 6 observed timestamp advances; no local drops/errors |
| Final `delivery-test` | PASS | Owned hidden-window callback age 2,516 ms; zero drops/errors; normal teardown completed |
| Final `isolation-test` | PASS | Both owned worker faults remain terminal; no shell activation in fault workers |
| Source boundary comparison to verified `origin/main` | PASS | Core, Persistence, their tests, both old spikes, global SDK pin and CI have no diff |
| Whitespace/diff review | PASS | Only new investigation files and root README link intended |

Cleanup succeeded on these short exercises; this is not a long-running leak or hardware
compatibility claim. Public native APIs were observed on one host only. Undocumented
desktop observation remains disabled on every other build/revision or architecture.

## Closeout review and revalidation — October 13, 2026

Reviewed the complete diff against refreshed `origin/main` and the repository instructions.
All requested evidence boundaries above are retained. The original `3901648` live results
remain historical observations; no foreground pass, role binding, complete coverage,
durable integration or full matrix is added by this closeout.

One correctness correction makes owned-helper shutdown failures explicit. Previously,
Dispose threw an InvalidOperationException for its own WaitForExit timeout and immediately
caught it. The corrected cleanup reports timeout and suppresses a kill race only after
confirming the process exited. Two portable regression tests cover timeout and exit-race
decisions; these are synthetic fault injections, not a real unkillable-process experiment.
The real owned-worker hang/malformed-output isolation checks also pass after the correction.

Closeout validation on the unchanged Professional 25H2 / 26200.9457 / x64 host:

| Check | Result | Evidence boundary |
|---|---|---|
| Observation `test` | 18 tests / 10,084 assertions PASS | Synthetic evidence, Core uncertainty and cleanup decisions; no Windows API invoked |
| Root `test` | Core 58 tests / 38,654 assertions; Persistence 56 tests / 544 assertions PASS | Synthetic Core; real SQLite plus injected faults and 5 owned actual Windows termination scenarios |
| VirtualDesktop `test` | 58 policy tests + 3 isolation checks PASS | No live switching or COM desktop behavior exercised |
| BrowserAttribution `test` | 2,053 assertions + 5 mocked groups; manifest/syntax/transport PASS | Synthetic caller, no real Chrome/Edge attribution |
| Observation `isolation-test` | Both fault workers PASS | Real owned child processes, no COM activation in fault workers |
| Observation and root `format` | PASS | SDK verify-no-changes; Release builds with zero warnings/errors |
| Scope and whitespace review | PASS | Core/Persistence contracts, old spikes, allowlists, CI and SDK pin unchanged |

No additional manual/live monitoring acceptance was performed during closeout. The failed
foreground stimulus, NOT RUN scenarios, zero live certification, and decision to stop before
durable accounting remain unchanged. Push and PR creation are now authorized; merging,
deployment, publishing, release, best-effort accounting and PR 4 implementation are not.
