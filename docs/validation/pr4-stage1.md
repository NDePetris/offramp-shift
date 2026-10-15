# PR 4 Stage 1 — owner-assisted observation gate

Status: live gate **PASS, owner-reported**; Stage 2 authorized by the owner.
The owner independently corroborated foreground, desktop and application identity.
The approved PR 4
break tolerance is 5 seconds consecutive / 15 seconds cumulative per incomplete
break; unknown time earns nothing. This Stage 1 change adds no accounting.

Base verified through GitHub: main `55ebea8079b5492faca0927bc3f6b9359e34464a`.
Branch: `feat/best-effort-activity-monitoring`. Desktop COM remains explicitly
enabled only by `user-test desktop`, on 26200.9457/x64. No switching, pinning,
movement, input injection, persistence, or activation request is performed by
the new exercises. Historical failed forced-foreground evidence remains unchanged.

## Run from ordinary Windows PowerShell

Build/test first, from the repository root:

```powershell
Set-Location G:\Projects\offramp-shift
./spikes/WindowsObservation/run.ps1 test
./spikes/WindowsObservation/run.ps1 user-test foreground
```

Two visible test windows are created. Click **Confirm** in Test A, then Test B,
alternating according to their step labels. There are 21 confirmations, yielding
20 user-driven transitions. Wait for the step label to advance before the next
click. Closing either window cancels the exercise. A ten-minute timeout bounds it.

Each confirmation compares the explicit button's owning window against a real
foreground/identity query and checks foreground hook receipt since the previous
confirmation. A 500 ms local drain permits asynchronous delivery; it does not
claim OS event completeness or a maximum delivery bound. Missing evidence, focus
changes during the read, slow processing, callback errors, or queue loss fail
explicitly. Only aggregate results are printed. Require result Pass **and exit 0**.

These two windows share a process/application key. This proves basic window focus
observation, not distinction between ordinary applications. Also perform this
application-identity smoke check before Stage 2:

1. Choose two ordinary, non-hosted applications with different executables
   (for example Notepad and a browser). Record anonymous labels A and B only.
2. Run `./spikes/WindowsObservation/run.ps1 watch 10` and manually focus A before
   the run ends. Keep it focused. Inspect the final opaque application key.
3. Repeat focusing B, then A again. Require available foreground/session/input
   evidence, zero drops/errors, distinct A/B keys, and the same A key on return.
4. Report which anonymous labels were used and whether each comparison passed.
   Do not paste paths, titles, browsing data, or extended activity captures.

## Desktop gate, including pinned-window behavior

```powershell
./spikes/WindowsObservation/run.ps1 user-test desktop
```

One Test A window is created. In Task View, **manually** set that test window to
show on all desktops. The probe does not change pin settings or infer desktop
identity from the window. Independently identify the intended Personal desktop
in Task View, select it, and click Confirm in Test A. Then select Work yourself
and confirm. Continue alternating Personal/Work according to the window's step
label until 21 confirmations complete. The same foreground window may remain
active across desktop changes.

The first two operator-labeled endpoints establish distinct ephemeral GUIDs.
Subsequent reads must match the expected GUID and contain both bindings in fresh
topology. A repeated/wrong/missing GUID or provider failure stops the run. GUIDs,
desktop names, process keys and event histories are not printed by this exercise.
The operator's independent role checks are required evidence; the harness cannot
tell whether the operator assigned the correct human roles initially.

When finished, test windows close and their temporary pin setting disappears
with them. No real application is closed or moved. Unsupported builds reject
before undocumented COM activation. Require result Pass and exit 0.

## Evidence to return

Return both final JSON summaries, the application-key comparisons (match/differ
only), and the following environment/independent checks:

- Windows build/UBR, architecture, SDK, monitor count/layout/DPI, ordinary user
  privileges, exact tested revision (or note the uncommitted Stage 1 working tree).
- Personal and Work independently corroborated in Task View at each confirmation.
- Test window pinned by the operator; current-desktop identity changed correctly
  despite unchanged foreground window.
- Any failure, unexpected focus behavior, unavailable provider, or omitted step.

Keep PASS, FAIL and NOT RUN separate. A wrong desktop or unstable application
identity blocks broader implementation. A failed stimulus or incomplete run is
not an accounting failure and does not count as a pass.

## Evidence boundary and remaining work

Synthetic tests validate gate bookkeeping and fail-closed decisions only. Native
snapshot/isolation checks validate selected reads and owned worker failure paths
only. Neither substitutes for user interaction. No Stage 2 Core confidence, idle,
break tolerance, checkpoint migration, durable host or performance implementation
has begun. No live totals, accuracy matrix or two-hour measurements exist yet.
Automatic switching and production browser attribution remain **NO-GO**.

## Preparation checks — October 13, 2026

- Observation assertion suite: 23 tests / 10,177 assertions PASS, synthetic only.
- Existing Core: 58 tests / 38,654 assertions PASS, synthetic observations.
- Existing Persistence: 56 tests / 544 assertions PASS; real SQLite, injected
  faults and five owned actual Windows termination scenarios reported separately.
- VirtualDesktop: 58 fake-policy tests plus three worker-isolation checks PASS;
  no live switching.
- BrowserAttribution: 2,053 synthetic assertions, five mocked extension groups,
  syntax/manifest and synthetic native transport PASS; no live browser attribution.
- Read-only `snapshot --desktop`: available foreground/input/session/desktop
  readings on 26200.9457/x64, two monitors, two desktops; no independent role or
  Task View corroboration. Zero certified accounting; no durable session opened.
- Observation `isolation-test`: owned stalled/malformed workers PASS, no COM in
  fault workers. This does not validate normal focus or desktop transitions.
- Root Release builds and formatting PASS. Initial restricted-sandbox observation
  compilation failed without a compiler diagnostic; outside-sandbox build passed
  with zero warnings/errors. Initial observation formatting found whitespace
  issues in the new summary initializer; those were corrected.

No raw snapshot identifiers are recorded here. The preparation-time NOT RUN
status is superseded by the following owner report; accounting remained untested.

## Owner-reported live acceptance and Stage 2 authorization

The owner supplied the following reviewed results in this implementation chat:

| Check | Reported result |
|---|---|
| Environment | Windows 11 26200.9457, x64, two monitors |
| Foreground | Pass; 21 confirmations / 20 transitions; 25 foreground events |
| Desktop | Pass; 21 confirmations / 20 transitions; 190 foreground events |
| Maximum event delivery age | 16 ms in each exercise |
| Loss/errors | Zero drops and callback errors in each exercise |
| Different application identity | A1 = A2; B differs; independent A/B/A check passed |

This is owner-reported live evidence, not an agent-executed acceptance run or
proof of historical completeness, accounting accuracy, other Windows builds,
long-running resource behavior or certification. Raw summaries, detailed
monitor layout/DPI, token details and exact tested working-tree revision were
not supplied in this authorization. Preserve those evidence limits. The owner
explicitly authorized Stage 2 on the existing branch without discarding Stage 1.
