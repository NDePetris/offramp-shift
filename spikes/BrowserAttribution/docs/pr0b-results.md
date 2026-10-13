# PR 0B evidence and decision — October 12, 2026

**NO-GO / not yet validated for production website attribution. Stop after this spike for review. PR 0 automatic desktop switching remains NO-GO unchanged.**

## What was established

The shared MV3 source, native stdio bridge, current-user local pipe collector, real Windows foreground observer, positive HWND/session correlation policy and conservative application fallback compile and pass the tests below. Domain-only reduction and rejection of unsafe samples have positive and negative simulations. Actual native process/stdio/pipe communication was exercised with a synthetic caller outside the Codex sandbox. A read-only live snapshot identified the foreground nonbrowser executable as `chatgpt` and correctly returned ineligible/application-only; no browser/window was moved.

This establishes implementation and protocol mechanics, **not** reliable active-website attribution in Chrome/Edge. The per-window popup bootstrap is provisional, not proven. The approved revised architecture plan was not present in the supplied checkout; the implementation follows the explicit PR 0B constraints and preserves existing PR 0 evidence. Exact conformity to any additional plan decisions cannot be certified until that plan is available for review.

Branch starts from PR 0 commit `9d10a8e` on `codex/virtual-desktop-feasibility`, retaining the isolated desktop probe. Dedicated branch: `codex/browser-attribution-feasibility`. No production features or changes to desktop switch gates.

## Recorded checks

| Check | Result | Evidence limit |
|---|---|---|
| .NET Release build | PASS, zero warnings/errors | .NET SDK 10.0.401; no external packages |
| Core policy/protocol assertions | 2,053 PASS | Fake windows/sessions/clocks, framing and domain schema |
| Mocked extension test groups | 5 PASS | Node 24.14.1; domain extraction, private/background refusal, navigation/permission races, minimal manifest |
| Worker/popup JavaScript syntax | PASS | Parsing only, not MV3 installation |
| Synthetic native transport | PASS outside sandbox | Real children, binary stdio, SID pipe, monitor singleton, unbound refusal, oversized-frame rejection, disconnect and restart; no valid browser binding |
| Native foreground snapshot | PASS outside sandbox | One ordinary nonbrowser observation, not foreground-change matrix |
| Inside-sandbox transport/snapshot | Restricted | Transport challenge timed out; process birth/name unavailable in snapshot. Same checks succeeded outside sandbox. Not a browser compatibility failure |
| Native registration/browser host launch | NOT RUN | Registry not installed; actual extension IDs unavailable |

The browser-control inventory exposed only Codex in-app browser/MCP Apps, no Chrome or Edge surface. Native UI control was unavailable. No test extension was installed in a real profile and no real-browser domain attribution was measured.

## Chrome and Edge separately

| Measure | Chrome | Edge |
|---|---|---|
| Actual controlled transitions | **0 / required >=200** | **0 / required >=200** |
| Actual incorrect-domain count | Unknown / not measured | Unknown / not measured |
| Actual positive website visibility | Not measured | Not measured |
| Actual settling latency | Not measured | Not measured |
| Pure simulated logical transitions | 250 | 250 |
| Simulated positive observations | 250 / 250 | 250 / 250 |
| Simulated wrong-domain assertions | Zero failures | Zero failures |
| Simulated timing | Synthetic 150 ms, not latency evidence | Synthetic 150 ms, not latency evidence |
| Window/profile/private/desktop matrix | Pending | Pending |

The two browser simulations only change an executable label in the policy fixture. They prove no browser-specific API behavior. They do demonstrate that the policy can admit positive observations rather than attaining zero failures by rejecting everything.

## Remaining uncertainty and gate status

- At least 200 real transitions per browser with zero incorrect attribution: **NOT MET**.
- Normal settling within one second: **NOT MEASURED**; intended 200 ms challenge/100 ms observation cadence is not evidence.
- Safe private/uninstrumented/ambiguous/stale/disconnected fallback: **simulated policy and synthetic disconnect paths PASS; live browser matrix pending**.
- No background website treated as foreground: **policy/mock checks PASS; live test pending**.
- Meaningful real website visibility: **NOT DEMONSTRATED**.

Unresolved: popup launch HWND semantics in each browser, ancestry with Windows native-host launching (including `.cmd` wrapper), profile-specific binding, browser ID collisions/lifecycle, background/native focus agreement, private windows across desktops, hook delivery lag and tab/navigation races, stale-domain intervals, worker crashes/reconnect, missing permissions, browser/OS/version changes and load at many profiles. See architecture document for the non-atomic temporal limit; short freshness deadlines cannot by themselves establish zero errors.

## Recommendation for PR 3

Retain the narrow architecture seam: a native authoritative foreground correlator consumes typed domain-only extension sessions via local Native Messaging/IPC and emits a nullable website classification with bounded freshness. **Do not proceed with production PR 3 website integration on these results.** First run the manual checklist and determine whether popup pairing is reliable and achieves useful visibility. If seamless attribution without per-window pairing/rebinding is required, the present candidate needs a validated automatic identity bootstrap or architectural change. Never use latest-report/title/geometry guesses as a substitute.

The current result is incomplete feasibility evidence, not a demonstrated browser-specific failure and not a GO. No push, merge or remote PR is authorized; this work ends with the local spike commit and review.
