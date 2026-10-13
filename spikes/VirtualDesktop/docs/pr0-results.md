# PR 0 results — October 12, 2026

## Decision

**Basic GUID-based desktop integration is feasible on the observed host. Production automatic switching is NO-GO / not yet validated.**

Recommend proceeding to the independent PR 0B browser-attribution experiment after owner review/authorization. Retain this desktop candidate for further manual validation rather than replacing it now. Do not proceed to a production switching capability on the evidence below.

This report closes the implementation of the PR 0 probe and available automated validation. It does **not** assert completion of the user-participation acceptance matrix. That matrix remains a prerequisite for the production go decision.

## Configuration and implementation

| Item | Observed value |
|---|---|
| Windows | Windows 11 25H2, exact build `26200.9457` |
| Architecture | x64 |
| Monitors | 2; layout/mixed-DPI/hot-plug acceptance not performed |
| Privileges | Ordinary user token; administrator membership false |
| Existing desktops | Two, named Personal and Work |
| Toolchain | Official SHA512-verified .NET SDK 10.0.401, repository-local and ignored |
| Provider | `shell-com-53f5ca0b-spike-v1` |
| Undocumented ABI reference | VirtualDesktopAccessor revision `ceb648849dcc5760a7f4ed223308ffe3de0b51c6` |

The OS registry's legacy ProductName reports Windows 10 Pro; DisplayVersion/build identify the tested Windows 11 configuration. Results use the exact build/UBR, not that legacy marketing string.

The C# console supervisor isolates COM activation/operations in a disposable hidden STA child. The production architecture is not instantiated. Bindings are GUID-based JSON with provider/build/version checks. Switching uses `FindDesktop(GUID)` followed by `SwitchDesktop(object)` and postcondition observation; no index navigation or keystroke injection exists.

## Evidence

| Check | Result | Evidence limit |
|---|---|---|
| Release build, warnings as errors | PASS, 0 warnings / 0 errors | Compilation only |
| Pure policy/protocol tests | 58 passed / 0 failed | Fake-provider tests, including 1,000 deterministic topology permutations |
| Worker fault isolation | 3 passed | Stalled worker killed, abnormal exit reported, malformed input rejected; fault paths never activate COM |
| Live negative requests | 7 passed | Unconfirmed, absent destination, current destination, stale monotonic lease, changed shell/source, and noninteractive expected state; each matched its intended rejection and attempted zero switches |
| Active identity/enumeration | PASS for ordinary current foreground | 20 captured snapshots across independently started workers; API reports stable Personal GUID and both identities |
| Persistent role bindings | PASS across probe/worker launches | JSON written/reloaded; both `bind` commands exercised on their corresponding desktops; reboot/Explorer persistence remains untested |
| Explicit GUID switching | 4 / 4 API-verified, zero observed wrong GUIDs | Two round trips; not 200 transitions; no independent Task View visual certification |
| Final source state | Personal GUID restored | Fresh snapshot after validation |

Switch timings:

| Run | Destination | Internal verification elapsed | End-to-end command elapsed | API result |
|---|---|---:|---:|---|
| Initial code | Work | 122 ms | 802 ms | Intended GUID observed |
| Initial code | Personal | 93 ms | 831 ms | Intended GUID observed |
| Final monotonic-lease code | Work | 83 ms | 758 ms | Intended GUID observed |
| Final monotonic-lease code | Personal | 82 ms | 727 ms | Intended GUID observed |

End-to-end timings include parent/worker startup and the source observation, not just the COM call. Raw local evidence remains in ignored `artifacts/` (`initial-roundtrip.json`, `roundtrip.json`, snapshots, bindings, and verification records). A sanitized summary is committed in `observed-results.json`. No application titles, URLs, screenshots, or input contents were collected.

## What has and has not been established

Established on this host:

- The pinned interface set activates and can enumerate desktops and retrieve the current desktop directly.
- GUID identities survive independent probe/worker process launches.
- The destination COM object resolves by GUID and can be used for one-shot switching.
- Tested policy failures reject before invoking switching; failures/timeouts do not trigger retries or corrective transitions.
- A public foreground window desktop ID is diagnostic only; policy does not use it as the active identity.

Not established:

- Active identity correctness for pinned windows, empty desktops, shell focus, elevated apps, and rapid changes, independently corroborated in Task View.
- GUID persistence over Windows reboot or Explorer restart.
- Real desktop rename/reorder/create/delete behavior and concurrent deletion races. Those cases have policy simulations, not live topology acceptance.
- Games, exclusive fullscreen, monitor hot-plug, mixed DPI, lock/sleep/resume, or another Windows build family.
- 99% verified completion within two seconds across at least 200 requests per claimed configuration.
- Memory/CPU/long-running leak behavior. A short-lived probe cannot certify production monitoring performance.
- Atomic cancellation if the user changes desktop immediately after the final source check.
- Guaranteed no shell-side failure from an undocumented ABI mismatch.

The object-based approach removes the obvious stale-index/reordering substitution mechanism. It does not prove a deleted object can never provoke unexpected shell behavior. A timeout can occur after the shell has accepted a request; killing the child does not undo it. Those cases are indeterminate and remain release risks.

## Supported and unsupported configurations

| Configuration | Probe policy | Validation status |
|---|---|---|
| `26200.9457`, x64, ordinary active session, existing Personal/Work, two monitors | Experimental probing and individually confirmed switches allowed | Basic observation/binding/switch feasibility demonstrated only |
| Same build, pinned apps, gaming, alternative monitor setup, topology/race/restart cases | Probe available for manual experiments | NOT VALIDATED |
| Other Windows builds/revisions | Undocumented COM disabled before activation | UNSUPPORTED by this spike; not a claim that Windows cannot support it |
| ARM64/x86, Windows 10, non-Windows | Disabled | UNSUPPORTED by this adapter |
| Missing/deleted bindings, mismatched provider/build/version, stale request, changed source/topology/focus/shell, noninteractive session | Reject switching | Automated policy and selected live rejection evidence |

**No configuration is certified for production automatic switching.** Upstream's advertised support for other builds is research context, not this project's validated support matrix.

## Objective go/no-go status

| Approved gate | Status |
|---|---|
| Active identity and distinct persistent roles | Partial: ordinary host/process restart only |
| At least 200 transitions per claimed configuration | NOT MET: 4 total, 2 on final code |
| Zero wrong destinations | No wrong API-observed destination in 4; required matrix remains unmet |
| At least 99% within 2 s | 4/4 within 2 s, insufficient sample |
| Topology, pinned, restart, race, monitor acceptance | NOT MET: manual checklist pending |
| No automatic retry/corrective loops | PASS for implemented paths and tested fault cases |
| Unknown-build guard and no window-movement operations | PASS by tests/code boundaries |
| Multiple Windows build families | NOT MET |

The next desktop step is the manual checklist, not enabling automatic switching. If live deletion/user-race tests expose a wrong destination, reconsider the provider or constrain the capability before production use. If identity remains reliable but switching cannot meet the gate, retain the approved notification-only fallback. If identity fails, desktop-scoped tracking must report unknown.

## Licensing and scope

GPLv3 is unchanged for development. Public distribution and final licensing remain open owner decisions. No push, remote PR, merge, production app, accounting code, or browser spike is part of this work.

## References

- Public API baseline: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager
- Pinned source interface definitions: https://github.com/Ciantic/VirtualDesktopAccessor/blob/ceb648849dcc5760a7f4ed223308ffe3de0b51c6/src/interfaces.rs
- Pinned GUID resolution/switch implementation reference: https://github.com/Ciantic/VirtualDesktopAccessor/blob/ceb648849dcc5760a7f4ed223308ffe3de0b51c6/src/comobjects.rs
