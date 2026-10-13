# PR 0C — focused Chrome discovery investigation

**Concrete manifest-generation defect corrected; real Chrome retest pending. Production website attribution and automatic desktop switching remain NO-GO.** This report supplements the initial Phase 1 evidence without overwriting PR 0B history or initial failed attempts. No full matrix, seamless-binding redesign or MVP implementation was performed.

## Owner-reported live failure

The owner reported a reproducible `Specified native messaging host not found` from Chrome `connectNative`, despite expected HKCU entries in both registry views, an existing manifest with the intended host name/origin/launcher, existing launcher and executable, no relevant policies, an isolated browser profile, and an independently working foreground monitor. Chrome's diagnostic log did not explain the failure. This is real owner-reported discovery failure, not a synthetic result. Number of repeated attempts and precise running version/channel were not supplied; do not invent counts.

## Independent inspection and concrete reproduction

The existing ignored manifest was inspected without printing its origins or path. Its name matched, its launcher was an existing `.cmd` file, it had no UTF-8 BOM, and **`allowed_origins` was `System.String`, not an array**. The registration expression reproduces that shape when Chrome and Edge IDs are equal: `Select-Object -Unique` emits one item, which PowerShell unwraps to a scalar before JSON serialization.

The corrected expression wraps the deduplicated output in `@(...)`. It retains one exact origin when IDs are equal and two when they differ, always as a JSON array. The surrounding host name, stdio interface, launcher, registration locations, permissions and binding rules are unchanged. The malformed local manifest is not rewritten by the tests; the owner retest preserves it before regenerating it with the corrected script.

## Chromium source review

Reviewed the source tag matching the previously observed installed Chrome file version, `154.0.8037.98`. That file-version observation is not independent confirmation of the owner's running version/channel.

- [Manifest parser, `NativeMessagingHostManifest::Parse`](https://raw.githubusercontent.com/chromium/chromium/154.0.8037.98/chrome/browser/extensions/api/messaging/native_messaging_host_manifest.cc): `allowed_origins` must be a list of strings. A string fails parsing even when it contains the correct extension origin. The host name and `stdio` type used here satisfy the inspected checks.
- [Common launch path, `LaunchInBackground`](https://raw.githubusercontent.com/chromium/chromium/154.0.8037.98/chrome/browser/extensions/api/messaging/launch_context.cc): manifest-load failure returns `RESULT_NOT_FOUND`. Origin authorization is checked later; launcher existence is also checked before process creation. Thus a schema failure explains “host not found” without a missing registry key/file. This is a source-backed explanation, not yet a successful real-browser retest.
- [Error mapping, `OnHostProcessLaunched`](https://raw.githubusercontent.com/chromium/chromium/154.0.8037.98/chrome/browser/extensions/api/messaging/native_message_process_host.cc): `RESULT_NOT_FOUND` maps to the native host not-found error; process-start failure has a separate result/error.
- [Windows discovery and launch](https://raw.githubusercontent.com/chromium/chromium/154.0.8037.98/chrome/browser/extensions/api/messaging/launch_context_win.cc): when permitted, lookup tries HKCU before HKLM and the 32-bit view before the 64-bit view. The manifest registry path must be absolute. The non-executable launch path invokes `COMSPEC`; direct executable launch is conditional on `.exe` and configuration. The existing `.cmd` wrapper is therefore not inherently rejected as an unsupported host type. Host arguments include extension origin and parent HWND; the working directory is the launcher's directory. No evidence justifies replacing the wrapper, adding registry locations or changing parent-HWND checks in this attempt.

## Automated verification

| Check | Outcome | Limit |
|---|---|---|
| New manifest regression against the pre-fix generator | Expected failure: scalar `allowed_origins` | Evaluates only the actual manifest hashtable expression; no registry writes |
| New manifest regression against corrected generator | PASS for equal/distinct IDs, deduplication, exact origins, host/type/launcher preservation | JSON generation only; no browser launch |
| Full BrowserAttribution validation outside sandbox | PASS, exit 0; zero build warnings/errors, 2,053 policy/protocol assertions, 5 mocked extension groups, syntax and synthetic transport checks | No real browser binding |
| Browser control inventory | Only Codex in-app browser and MCP Apps | No callable Chrome/native UI for owner extension actions |
| One corrected real Chrome transport/binding attempt | PENDING owner interaction | No positive attribution claimed |

The registration regression runs under the existing browser test command, including its Windows CI job. It evaluates the real hashtable via PowerShell AST to avoid running surrounding file/registry operations; no helper service, registry fixture installation or new diagnostic framework was introduced. Historical PR 0B evidence and the VirtualDesktop tree remain unchanged.

## Retest boundary and recommendation

Use the [one Chrome retest](pr0c-chrome-discovery-retest.md): preserve the old manifest, regenerate with real IDs, verify array shape, receive one genuine browser-launched challenge, then attempt one popup binding and positive domain observation if transport passes. Keep all domain-bearing evidence ignored locally. Native transport success alone is not domain/HWND attribution; a “Bound” response alone is not independent proof of parent HWND/ancestry.

Do not add speculative launcher changes or repeated retries if discovery remains unresolved. Stop for review and recommend deferring website attribution if this corrected attempt cannot establish credible attribution. Binding failures must be reported with uncertainty; absent corroboration is not proof that the HWND architecture fundamentally failed. Do not substitute title/position/latest-report heuristics.

The owner has decided that website-level attribution is **not an MVP prerequisite**. Application-level context detection with manual-first desktop control is under consideration. Even if this single test succeeds, website attribution stays experimental and optional, and both production NO-GO gates remain intact. No MVP implementation is authorized by this investigation.
