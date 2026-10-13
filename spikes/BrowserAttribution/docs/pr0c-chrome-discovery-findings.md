# PR 0C — focused Chrome discovery investigation

**Concrete manifest-generation defect corrected; Chrome transport and Bound were owner-reported, but positive domain attribution remains UNVERIFIED after the owner's clarification. The reviewed local capture contains no domains. Production website attribution and automatic desktop switching remain NO-GO.** This report supplements the initial Phase 1 evidence without overwriting PR 0B history or initial failed attempts. No full matrix, seamless-binding redesign or MVP implementation was performed.

## Owner-reported live failure

The owner reported a reproducible `Specified native messaging host not found` from Chrome `connectNative`, despite expected HKCU entries in both registry views, an existing manifest with the intended host name/origin/launcher, existing launcher and executable, no relevant policies, an isolated browser profile, and an independently working foreground monitor. Chrome's diagnostic log did not explain the failure. This is real owner-reported discovery failure, not a synthetic result. Number of repeated attempts and precise running version/channel were not supplied; do not invent counts.

## Independent inspection and concrete reproduction

The existing ignored manifest was inspected without printing its origins or path. Its name matched, its launcher was an existing `.cmd` file, it had no UTF-8 BOM, and **`allowed_origins` was `System.String`, not an array**. The registration expression reproduces that shape when Chrome and Edge IDs are equal: `Select-Object -Unique` emits one item, which PowerShell unwraps to a scalar before JSON serialization.

The corrected expression wraps the deduplicated output in `@(...)`. It retains one exact origin when IDs are equal and two when they differ, always as a JSON array. The surrounding host name, stdio interface, launcher, registration locations, permissions and binding rules are unchanged. The malformed local manifest is not rewritten by the tests; the owner retest preserves it before regenerating it with the corrected script.

## Chromium source review

Reviewed the source tag matching the previously observed installed Chrome file version, `154.0.8037.98`. That file-version observation is not independent confirmation of the owner's running version/channel.

- [Manifest parser, `NativeMessagingHostManifest::Parse`](https://raw.githubusercontent.com/chromium/chromium/154.0.8037.98/chrome/browser/extensions/api/messaging/native_messaging_host_manifest.cc): `allowed_origins` must be a list of strings. A string fails parsing even when it contains the correct extension origin. The host name and `stdio` type used here satisfy the inspected checks.
- [Common launch path, `LaunchInBackground`](https://raw.githubusercontent.com/chromium/chromium/154.0.8037.98/chrome/browser/extensions/api/messaging/launch_context.cc): manifest-load failure returns `RESULT_NOT_FOUND`. Origin authorization is checked later; launcher existence is also checked before process creation. Thus a schema failure explains “host not found” without a missing registry key/file. The source explanation and the owner's subsequent retest report are distinct evidence.
- [Error mapping, `OnHostProcessLaunched`](https://raw.githubusercontent.com/chromium/chromium/154.0.8037.98/chrome/browser/extensions/api/messaging/native_message_process_host.cc): `RESULT_NOT_FOUND` maps to the native host not-found error; process-start failure has a separate result/error.
- [Windows discovery and launch](https://raw.githubusercontent.com/chromium/chromium/154.0.8037.98/chrome/browser/extensions/api/messaging/launch_context_win.cc): when permitted, lookup tries HKCU before HKLM and the 32-bit view before the 64-bit view. The manifest registry path must be absolute. The non-executable launch path invokes `COMSPEC`; direct executable launch is conditional on `.exe` and configuration. The existing `.cmd` wrapper is therefore not inherently rejected as an unsupported host type. Host arguments include extension origin and parent HWND; the working directory is the launcher's directory. No evidence justifies replacing the wrapper, adding registry locations or changing parent-HWND checks in this attempt.

## Automated verification

| Check | Outcome | Limit |
|---|---|---|
| New manifest regression against the pre-fix generator | Expected failure: scalar `allowed_origins` | Evaluates only the actual manifest hashtable expression; no registry writes |
| New manifest regression against corrected generator | PASS for equal/distinct IDs, deduplication, exact origins, host/type/launcher preservation | JSON generation only; no browser launch |
| Full BrowserAttribution validation outside sandbox | PASS, exit 0; zero build warnings/errors, 2,053 policy/protocol assertions, 5 mocked extension groups, syntax and synthetic transport checks | No real browser binding |
| Browser control inventory | Only Codex in-app browser and MCP Apps | No callable Chrome/native UI for owner extension actions |
| Corrected real Chrome retest | Transport PASS and Bound owner-reported; positive domain attribution UNVERIFIED | Owner later expressed uncertainty; reviewed capture has no domains |

The registration regression runs under the existing browser test command, including its Windows CI job. It evaluates the real hashtable via PowerShell AST to avoid running surrounding file/registry operations; no helper service, registry fixture installation or new diagnostic framework was introduced. Historical PR 0B evidence and the VirtualDesktop tree remain unchanged.

## Owner-reported retest and local capture review

The owner initially said the correction appeared to work, then selected a response confirming transport passed, popup reported Bound, domain matched the foreground test page, and focus-away returned null. After the capture discrepancy was raised, the owner clarified that perhaps the domains were not successfully captured. Preserve that sequence, but do not retain a positive-domain PASS on the uncertain response. Transport and Bound remain owner-reported; successful real domain attribution is **UNVERIFIED**. No exact transition count, independent parent-HWND/ancestry attestation, or running Chrome version/channel was supplied. Edge remains untested in this focused follow-up.

Independent read-only inspection found the current local manifest now has a one-element `allowed_origins` array. The latest documented retest capture, ignored `artifacts/chrome-retest-20261012-225819-213/observations.jsonl`, contained 1,695 observation lines, zero malformed lines, and **zero Chrome observations with a domain**. Its 468 nonbrowser observations also all had null domain. Polling lines are not controlled transitions. This capture therefore supports application-only behavior during its recorded intervals, not the reported positive attribution; do not turn its all-null result into a positive pass or a zero-error validation claim.

The clarification did not identify an alternate positive capture or running version/channel. The available evidence cannot distinguish failed attribution from a successful observation outside the recorded interval. No additional test is requested. No actual domain values, raw captures, extension IDs or profile paths were copied into this report.

## Subsequent stable-page positive test

The owner requested one more test and was given a fresh-capture, single-window Chrome procedure: reload after monitor startup, load a stable benign test page, bind once, then leave the browser foreground for ten seconds. The owner returned **524 Chrome observations, zero expected-domain observations and zero unexpected-domain observations** from the prescribed capture summary. The popup status, running browser version/channel and actual interval duration were not supplied with these counts.

This attempt **fails the positive-attribution criterion**. All reported Chrome domains were null; no recorded observation establishes website visibility. Zero unexpected domains in an all-null run is not an attribution safety pass. The counts are polling observations, not 524 controlled transitions. These owner-reported counts do not identify the failure boundary or prove that the architecture is fundamentally unreliable. No new code, diagnostics or further test was initiated in response. Historical reports and earlier owner uncertainty remain preserved above.

## Retest boundary and recommendation

The [one Chrome retest](pr0c-chrome-discovery-retest.md) is documented, and the owner has reported transport and binding outcomes, with positive-domain uncertainty as recorded above. Do not repeat it simply to resolve the capture discrepancy. Keep all domain-bearing evidence ignored locally. Native transport success alone is not domain/HWND attribution; a “Bound” response alone is not independent proof of parent HWND/ancestry.

The evidence supports the concrete manifest correction and owner-reported progress through transport/binding, but does not establish real Chrome domain-to-HWND attribution. Stop for review now. Phase 1 acceptance, seamless binding, profile/window isolation and production reliability remain unestablished. Recommend keeping website attribution deferred from the MVP and considering application-level context with manual-first desktop control only in a separately authorized task.

Do not add speculative launcher changes or repeated retries if discovery remains unresolved. Stop for review and recommend deferring website attribution if this corrected attempt cannot establish credible attribution. Binding failures must be reported with uncertainty; absent corroboration is not proof that the HWND architecture fundamentally failed. Do not substitute title/position/latest-report heuristics.

The owner has decided that website-level attribution is **not an MVP prerequisite**. Application-level context detection with manual-first desktop control is under consideration. Even if this single test succeeds, website attribution stays experimental and optional, and both production NO-GO gates remain intact. No MVP implementation is authorized by this investigation.
