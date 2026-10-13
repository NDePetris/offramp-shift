# Windows PC validation checklist

For PR 0C, first run only the [Phase 1 smoke test](pr0c-phase1-smoke.md) and stop for review. The matrix below is conditional on reviewed Phase 1 evidence and a credible binding approach; it is not authorized by an automated pass or a single successful popup pairing.

When the full matrix is separately authorized, run separately for Chrome and Edge, then together. **At least 200 controlled transitions per browser**; count a browser's foreground entry/tab/navigation as one transition, not every polling line. A failed transition remains in the denominator. Stop on the first incorrect domain, preserve sanitized evidence, and keep NO-GO.

## Setup and first positive gate

- [ ] Record exact Windows build, browser versions/channels, .NET/Node versions, ordinary privilege level, monitors/DPI, test profile labels and desktop labels. No profile paths/names from real personal accounts in committed evidence.
- [ ] Follow README setup with disposable test profiles. Start a fresh capture filename; never overwrite earlier evidence. Run tests from normal PowerShell outside Codex sandbox.
- [ ] Prepare distinct public test hostnames: `example.com`, `example.org`, `example.net` (or other benign HTTP(S) hosts you control). Record expected hostnames only. Avoid personal browsing; do not record full URLs, titles or page contents.
- [ ] In Chrome window A, require binding success, close popup, load a test domain and verify native output has `application:"chrome"`, correct HWND/PID and the expected domain. Repeat in Edge with `application:"msedge"`.
- [ ] Verify the popup can bind while open and the domain appears after closing it. Corroborate actual parent HWND correspondence and process ancestry independently when claiming those assumptions established; a successful binding plus foreground output only establishes that the implemented checks accepted the observation. If binding never works, stop for diagnosis/review; do not loosen identity checks or declare a fundamental architecture failure without identifying the cause.
- [ ] Missing tabs permission can be tested in a disposable copy of the extension with `tabs` removed, loaded/re-registered under its new ID. Require null domain; unload it afterward and restore the original IDs. Never widen permissions to make tests pass.

## 200 transitions for each browser

Use this allocation independently for Chrome and Edge (totals 200). Track positive-domain and expected-fallback observations separately. Most ordinary supported transitions must actually expose the right website; zero errors with all nulls does not pass.

| Scenario | Transitions per browser | Required observation |
|---|---:|---|
| One profile, two bound windows, multiple tabs | 40 | Correct domain for actual foreground window/tab; include identically positioned/maximized windows |
| Two instrumented profiles, plus one uninstrumented profile | 30 | Bind every instrumented window; different domains per profile; uninstrumented always null |
| Chrome and Edge simultaneously | 30 | Alternate foreground browsers; background browser reports must never overwrite foreground |
| Background tab navigation, reload and focus loss to ordinary nonbrowser app | 20 | Null on nonbrowser foreground; background activity never becomes foreground domain; record conservative visibility drops |
| Two virtual desktops | 20 | Move windows manually in Task View, switch desktops manually, compare actual foreground; no probe switching; test pinned windows separately |
| Incognito/InPrivate | 20 | Probe cannot run there; private foreground always null even if ordinary window has same test hostname; return to ordinary profile recovers |
| Rapid focus/tab/navigation races | 20 | Alternate windows/tabs/domains faster than 200 ms, include away-and-back, redirects and pending loads; no wrong domain on any observed sample |
| Host disconnect/restart, stale response and missing permission | 20 | Kill only BrowserProbe host child (not browser/monitor), or unload extension; null until reconnected/rebound; no remembered domain |

Also test browser window close/reopen, tab drag between windows, worker termination, monitor restart, lock/unlock, sleep/resume, new profile windows and unknown browser/internal/file pages. Keep these additional transitions in the evidence. Window lifecycle/desktop tests require owner participation; the automated tests do not simulate real Task View behavior.

For host restart: use Task Manager's executable name/command line to identify BrowserProbe child launched by the browser; avoid killing the monitor. No full browser command line/profile arguments should be saved. Test both abrupt termination and closing a Native Messaging port. Popup reconnect rotates session and requires all bindings to be renewed. For stale-message testing, automated framing/policy tests already cover wrong nonce/deadline; a live debugger delay on a **sanitized domain-only message** can corroborate it, but must be separately labelled. Do not mistake that replay for a controlled browser transition.

## Evidence and timing

Use `validation-template.csv` as a ledger. Populate one row per controlled transition, with browser version, scenario, expected application/domain or intentional null, observed domain, whether any wrong-domain sample occurred during the entire transition, positive visibility, transition monotonic timestamp, first correct settled timestamp, latency and reviewer initials. Keep all intervening polling lines in ignored local evidence. An expected-fallback observation emitting a domain is an error. A domain from the previous tab/window during a transition is also an error; do not hide it by examining only the eventual settled value.

For focus changes, correlate the new native HWND/focus epoch with independently witnessed browser focus. For tab/navigation changes, independently timestamp the user action with a stopwatch/test operator; there is no browser-independent tab-action marker in this probe. Native invalidation-to-positive timing alone omits delivery delay and cannot establish end-to-end latency. Use timestamps in the same monotonic clock domain or document synchronization uncertainty. Record p50/p95/max and fraction within one second for supported stable observations; exclude intentional unsupported cases only from latency/visibility denominators, never from safety checks. Loading-to-complete time should be reported separately from post-load settling.

Show each ordinary scenario has repeatable positive attribution. Report `positive supported observations / supported observations`, plus total fallback count by scenario/reason. “Meaningful visibility” has no numerical threshold in the supplied acceptance criteria; do not manufacture an approved percentage or pass an all-null run. Decide that gate from the measured coverage and explicit review. “Normally within one second” also needs the measured distribution, not just a polling interval.

## Final review

- [ ] Chrome: >=200 actual controlled transitions, zero wrong domains, timing distribution and positive coverage recorded.
- [ ] Edge: >=200 actual controlled transitions, zero wrong domains, timing distribution and positive coverage recorded.
- [ ] Unsupported/private/uninstrumented/stale/disconnected cases all stay application-only.
- [ ] No background website becomes foreground activity; rapid-transition samples reviewed for transient errors.
- [ ] Domain-only native payload/output verified with sanitized test inputs; full URL/title/page data absent from saved evidence.
- [ ] CPU/memory/profile fanout observations and hook/ancestry/bootstrap limitations recorded, not assumed solved.
- [ ] If any wrong domain appears or useful coverage cannot be demonstrated: NO-GO; revise identity/temporal architecture before the future production website-integration phase.
- [ ] Sanitize summary; do not commit raw logs. Unregister host, unload extensions and stop monitor when finished.

PR 0B passing later would not change PR 0's separate automatic-switch NO-GO. No push/PR/merge without owner approval.
