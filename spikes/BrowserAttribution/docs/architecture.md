# Candidate architecture and security boundary

## Identity rather than latest-report attribution

`chrome.windows` IDs are scoped to a browser/profile context and are not HWNDs. Browser PID alone cannot identify a profile/window; profiles may share the browser process. Titles, URL text in native windows, timing proximity, and window rectangles are not identity proofs. This spike avoids those guesses.

The candidate bootstrap is an explicit action popup in each ordinary window. Native Messaging launch metadata supplies `--parent-window`; the service worker launches separately with zero. The popup reports only its extension window ID and the worker's random session nonce. The native bridge strips/rejects extension-supplied parent metadata and adds the launch HWND. The monitor requires an eligible visible, non-minimized, non-cloaked Chrome/Edge top-level window, foreground corroboration, one worker for the nonce, the probe executable as binder, and browser PID ancestry for both worker and binder. It binds `(session nonce, extension window ID)` to `(HWND, owning PID, process birth, destruction generation, browser executable)` in memory. It does not persist profile names, paths, or browser window IDs across restart.

**This is a hypothesis awaiting real browser validation:** the action popup's launch parent must be the actual containing browser HWND; extension window ID, process ancestry and popup focus semantics must agree. Parent HWND metadata was designed for UI parenting, not an attested attribution API. A successful native snapshot/pipe test cannot validate that assumption. Zero parent, unexpected ancestry, popup child handles or unsupported classes reject. No geometry/title matching fallback exists.

The monitor observes actual `GetForegroundWindow`, polls every 100 ms and installs out-of-context foreground/destroy WinEvent hooks on a message-pump thread. Foreground epochs discard candidates, including observed away-and-back focus changes. Destroy generations and process birth distinguish many HWND/PID reuse cases. Foreground membership and DWM cloaking avoid assuming the last focused extension window is on the active virtual desktop. No desktop COM or switching code is called. The virtual-desktop/pinned-window behavior is still a live test obligation.

Every 200 ms, each connected worker receives a random challenge. A worker samples only its focused ordinary window, checks the active tab twice, rechecks focus and its event epoch, and reduces HTTP(S) hostname to lowercase ASCII/punycode. Loading, pending navigation, unsupported scheme, IP literal, localhost, missing URL/permission, focus loss and private observations return no domain. All tab/window change events invalidate rather than transmit event objects. Invalidations are deliberately broad: background activity can reduce visibility but cannot promote a background website.

A reply needs the current exact native window binding, same foreground epoch, matching one-use nonce, correct session/window ID, stable/focused/nonprivate sample and valid hostname. Native reply deadline is 400 ms; candidate lease is 500 ms. A stream disconnect removes candidates/bindings. A closed window removes its extension binding. Conflicting bindings are revoked rather than allowing the earliest report to win. Multiple fresh candidates reject. Worker or monitor restart loses all binding state. Application classification is always available when process observation succeeds; failed process inspection produces `unknown`.

## Temporal limits

Native and browser events are asynchronous, and no API transaction atomically reads foreground, active tab and hostname together. WinEvent delivery can lag; a tab can change after the final extension read. A previously accepted domain can exist until its invalidation arrives or its lease expires. Polling/output delivery adds delay; the 500 ms lease is an internal state limit, not a measured end-to-end guarantee. A stalled stdout consumer can stop monitor progress. Long queues, hook loss, lock/sleep/resume, DPI, elevated windows, HWND reuse and many connected profiles need explicit tests. The generation map is not bounded, and the probe does no long-running resource certification. **Do not interpret this protocol as proof of zero transient misattribution.** Any observed wrong domain in the controlled live tests is a NO-GO, even if it quickly corrects.

The current loop sends sequentially with a 100 ms write deadline per connection; many blocked profiles could exceed the normal one-second target. Native host reconnect is manual. These are spike limitations, not production defaults.

## Minimal permissions and data

Only `tabs` and `nativeMessaging`. `tabs` is necessary for unattended active-tab URL visibility: `activeTab` alone grants temporary user-invoked access and would test a different capability. No host permissions, content scripts, `webRequest`, `webNavigation`, debugger, scripting, storage, page contents, title scraping, screenshots, or TCP/HTTP listeners. No outbound telemetry.

The browser API necessarily returns a full URL transiently inside the extension to derive a hostname. It is never serialized, stored or logged by the probe; only the hostname leaves that context. The permission also makes title fields accessible, but the code never reads them. Domain is a complete hostname (including subdomain), not eTLD+1; no public-suffix list is embedded. Credentials, ports, paths, queries and fragments are removed. Private mode is `not_allowed`; its uninstrumented HWND has no binding and must remain application-only. Browser-owned full tab objects do not enter native JSON. The fixed native DTO rejects unknown properties, malformed domains, wrong versions/tokens, oversized/truncated frames; raw rejected payloads/errors are not echoed. Frames are UTF-8 JSON with four-byte little-endian lengths and a stricter 4 KiB spike limit.

## Local trust/security implications

Host registration uses exact extension origins with no wildcard in two named HKCU keys. Both browser IDs may differ. Edge's documented fallback search can use Chrome registrations, so explicit Edge registration avoids accidental dependence on that fallback. The bridge uses redirected binary stdin/stdout and a SID-scoped current-user named pipe; named-pipe client PID corroborates local launch identity. No elevation, network listener, persistent secrets, autostart or profile filesystem inspection.

An ordinary same-user process can impersonate parts of this experiment, read its evidence, alter unpacked extension code/registration or potentially replace a pipe server. The bridge's origin shape validation is not an authentication signature. The browser's host allowlist enforces the intended extension origin on genuine launches, but CLI callers can forge an origin. This is not a security boundary against a compromised user/browser/extension, and process names are not signed publisher validation. The transport tests deliberately use a synthetic CLI caller and cannot obtain a valid HWND binding. A production installer needs immutable signed binaries, protected installation/registration, explicit per-user IPC permissions, server/client identity checks, browser executable provenance and bounded queues/resource usage. Domains alone can be sensitive; local raw evidence is ignored and should be minimized.

## Recommended production seam (subject to the gate)

Keep native foreground/window observation authoritative in a single app-owned correlator. Receive typed versioned domain-only observations from per-profile extension sessions through stdio hosts and authenticated local IPC. Enforce positive identity, freshness, disconnect invalidation and application-only fallback there; do not attach the newest extension report directly to a timer. Pass nullable domain plus reason/observation lease into a future classification boundary. Do not integrate PR 0 desktop switching with this experiment.

First validate popup pairing across both browsers. If explicit per-window pairing/rebinding is unacceptable for PR 3, this candidate **requires an architectural change or a validated automatic identity bootstrap** before production website attribution. A zero-parent worker cannot silently infer an HWND. Consider browser-specific supported identity mechanisms only after evidence; do not substitute title/geometry heuristics. Application-only behavior can remain the safe baseline. None of those production components are implemented here.

## Primary references checked October 12, 2026

- [Chrome Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging): stdio framing, origin allowlist, parent HWND and service-worker zero value.
- [Edge Native Messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging): Edge registration, IDs and fallback lookup order.
- [Chrome windows API](https://developer.chrome.com/docs/extensions/reference/api/windows): focused/window ID and private state semantics.
- [Chrome worker lifecycle](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle): native connection lifetime/restart considerations.
- [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook): asynchronous delivery and required message loop.
- [GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow): actual foreground handle, including transient null.
