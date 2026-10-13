# PR 0C Phase 1 evidence — October 12, 2026

**INCOMPLETE: owner-assisted live validation pending. Production browser attribution and PR 0 automatic desktop switching remain NO-GO.** No Phases 2 or 3 have been performed. The absence of real results is not a demonstrated browser compatibility failure.

## Scope and provenance

Branch `spike/browser-attribution-live-validation` starts at verified latest `main`, `5b4a116a42afcdffbb30be92c64de2f01f87f1c8` (GitHub PR #3, engineering harness). The owner approved Phase 1 only, prioritizing a first real smoke test over speculative diagnostics. PR 0B historical evidence and machine-readable results remain unchanged. Its historical “PR 3 website integration” references refer to the future production website-integration phase, not GitHub PR #3.

## Independently observed configuration

| Item | Observation | Limit |
|---|---|---|
| Windows registry | DisplayVersion 25H2; build 26200.9457; x64 | Current machine, not a live browser run |
| .NET SDK | 10.0.401, repository-local | Automated baseline |
| Node.js | 24.14.1 | Automated baseline |
| Installed Chrome file version | 154.0.8037.98 | Running version/channel not independently checked |
| Installed Edge file version | 155.0.4283.45 | Running version/channel not independently checked |
| Browser/UI control inventory | Only Codex in-app browser and MCP Apps; native app inventory empty | No callable Chrome/Edge or native UI surface available |
| Host registration preflight | Probe host absent in HKCU/HKLM, both 32-bit and 64-bit views, for Chrome/Edge/Chromium | Read-only inspection; actual registration/browser lookup not exercised |
| Live privilege, monitor/DPI setup | Not recorded for a browser run | Owner must record during smoke test |

## Automated results and failed attempt

| Check | Result | Evidence limit |
|---|---|---|
| VirtualDesktop Release build | PASS, zero warnings/errors | No shell COM/live switching |
| VirtualDesktop policy and worker isolation | 58 assertions and 3 isolation checks PASS | Fake policy and deliberate child fault paths |
| BrowserAttribution Release build | PASS, zero warnings/errors | Compilation only |
| Browser policy/protocol | 2,053 assertions PASS | Simulated windows/sessions/clocks |
| Mocked extension tests | 5 groups PASS | Domain reduction and sampling tests, not actual extension installation |
| Worker/popup syntax | PASS | Parsing only |
| Synthetic native transport, initial sandbox attempt | FAIL: timeout at “challenge bridged” | Preserved failed attempt; not a real browser caller |
| Same full browser baseline outside sandbox | PASS, exit 0 | Real disposable child processes/stdio/named pipe; synthetic caller; no binding |
| Synthetic transport with system .NET root | FAIL before monitor ready: system installation has only .NET 6/8, missing required .NET 10 | Separate runtime-path experiment; no browser caller or binding |
| New CI job | Configured to run the same browser test command on Windows with pinned SDK and Node 24 | GitHub Actions run not performed; no push/PR authorized |

The sandbox timeout and outside-sandbox pass are consistent with the restrictions previously recorded in PR 0B. They do not establish actual Chrome/Edge behavior. The policy suite still prints 250 synthetic positive transitions per executable label; none count toward real browser results.

The system-runtime failure confirms the repository-local SDK runtime is necessary on this machine; it does not invalidate browser binding. CI explicitly derives `DOTNET_ROOT` from the `dotnet` executable installed by its setup step. The local-runtime `.cmd` launcher still needs a genuine browser launch. Smoke instruction PowerShell blocks were parsed without executing browser commands; local documentation links and ignored capture/profile paths were checked. Historical evidence and the VirtualDesktop tree were checked unchanged.

## Real browser results

| Requirement | Chrome | Edge |
|---|---|---|
| Disposable-profile extension installation | NOT RUN | NOT RUN |
| Actual native-host launch/registration | NOT RUN | NOT RUN |
| Popup binding attempt | NOT RUN | NOT RUN |
| Independently corroborated positive domain/HWND | NOT MEASURED | NOT MEASURED |
| Repeat positive after focus returns | NOT RUN | NOT RUN |
| Focus-away/unbound fallback | NOT RUN | NOT RUN |
| Extension disconnect/reload/rebind | NOT RUN | NOT RUN |
| Wrong-domain count | Unknown | Unknown |
| Actual controlled transitions | 0 | 0 |

The [owner-assisted smoke procedure](pr0c-phase1-smoke.md) supplies exact disposable-profile launch, installation, registration, capture, limited positive/fallback/lifecycle checks and cleanup instructions. An owner result request was issued because callable real browser control was unavailable. No synthetic substitute, guessed extension ID, production profile inspection or registry installation was used.

## Changes and architectural findings

Added the focused smoke procedure, separate evidence report, links and Phase 1 review gates, and an automated-only browser CI job. Corrected the checklist's output-field notation and its implication that a successful binding independently proves parent HWND/ancestry. No native protocol, permissions, binding predicates, timing limits or extension behavior changed; no diagnostic infrastructure was added without an observed live failure. Existing regression suites cover the unchanged behavior.

The first live gate must resolve whether popup launch parent HWND, extension window ID, foreground semantics and both native-host process ancestries agree. The local SDK uses a `.cmd` launcher; its actual browser launch remains untested. A worker hello can race a popup bind. Multiple profiles can share a browser process, so PID alone is not profile identity. Asynchronous tab/native events, hook delay and output stalls prevent interpreting the current lease as a proof of zero transient misattribution. Manual pairing does not establish seamless production binding.

## Recommendation and stop point

Complete the first owner-assisted attempt before adding instrumentation. If it fails, retain the failed attempt and instrument only the rejected boundary needed to explain it. Stop immediately for review if evidence contradicts the identity model, any wrong domain appears, unsafe data is captured, or progress requires weaker checks, unsupported guesses, new permissions or increasingly complex workarounds.

Phase 1's acceptance criteria are **not met** while both real-browser positive/fallback checks are pending. Review the real evidence before any seamless-binding assessment or 200-transition matrix. Keep raw domains/captures/ledgers in ignored local artifacts; committed summaries use anonymous test-host labels. Nothing in this report changes either production NO-GO gate.

## Subsequent owner-reported discovery failure

The owner later reported reproducible Chrome native-host discovery failure despite expected registration and existing files. A focused follow-up found the generated allowlist was a JSON string rather than an array. See the [separate source review, correction and retest status](pr0c-chrome-discovery-findings.md) and [one owner-assisted Chrome retest](pr0c-chrome-discovery-retest.md). The tables above retain the initial preparation results; they are not a current claim that the owner never installed or tested Chrome. The owner initially confirmed transport passed, Bound, correct domain and focus-away null, but subsequently expressed uncertainty about domain capture after review found an all-null log. Transport and Bound remain owner-reported; positive domain attribution is UNVERIFIED. The follow-up report preserves the sequence and evidence limits. Edge and broader Phase 1 acceptance remain pending. Website attribution is no longer an MVP prerequisite; MVP implementation remains outside this work.

The owner subsequently requested and ran a stable-page positive test, reporting 524 Chrome observations with zero expected-domain and zero unexpected-domain observations. That attempt fails the positive-attribution criterion; the popup status was not supplied for this attempt. An all-null run does not establish attribution safety or successful domain visibility. See the follow-up report for the preserved counts and limitations. No further test, diagnostic implementation or production work followed this result.
