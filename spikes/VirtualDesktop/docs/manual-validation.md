# Manual Windows validation checklist

Record date, exact Windows build/UBR, architecture, app commit, SDK, monitor count/layout/DPI, shell configuration, and applicable game/version/rendering mode. Use ordinary user privileges. Retain raw JSON separately from the reviewed result summary. Mark each item PASS, FAIL, or NOT RUN, never infer PASS from unit tests.

The currently executable ABI allowlist is 26200.9457/x64 only. A second build needs a separately reviewed adapter/allowlist change; do not bypass that guard to fill in this checklist. Production support cannot be claimed from one host.

## Identification and bindings

- [ ] Independently compare `snapshot.current` with Task View for Personal, Work, and a third unbound desktop.
- [ ] Repeat with an ordinary foreground application, empty desktop, shell/Task View, and an elevated application. Transition-time unknown/rejected results are acceptable; a wrong confirmed GUID is not.
- [ ] Bind each role with the documented commands. Close/restart the probe and verify both GUIDs.
- [ ] Rename each desktop; verify the same GUID bindings remain valid.
- [ ] Reorder desktops in Task View; verify bindings remain valid and a fresh explicit switch still reaches the bound GUID.
- [ ] Create a third desktop, then repeat; indices/names must not become destination identities.
- [ ] Delete Work; `verify` and `switch Work` must reject without transition. Creating a new desktop with the same name must not repair the binding implicitly.
- [ ] Restart Explorer yourself with saved work; capture new shell identity and revalidate. A request based on the old shell must reject.
- [ ] Reboot Windows; revalidate GUID persistence. Reboot has not been inferred from new helper processes.

## Pinned windows and focus

- [ ] In Task View, pin a test window to all desktops. Keep it foreground while manually switching; independently queried current GUID must change correctly even if its window membership is ambiguous.
- [ ] Repeat with an entire application pinned to all desktops. Restore original pin settings afterward.
- [ ] Rapidly change focus and manually switch desktops while running `watch 30`. Mixed observations should fail safely instead of being labeled as a valid active desktop.
- [ ] Change focus/source while an explicit switch is pending. Before-call changes reject; after-call races are reported without retries. Observe for any transient wrong destination.
- [ ] Delete/reorder the destination while a request is pending, using disposable desktops and saved work. Record shell behavior; object/GUID resolution must never turn into ordinal fallback.

## Safety and configuration

- [ ] Run `safety-test` and `isolation-test`; zero attempted switches for deliberately invalid requests; parent survives helper failure.
- [ ] Lock/unlock and sleep/resume; snapshot can fail/mark noninteractive while unavailable. No old switch request resumes automatically.
- [ ] Test one monitor and two or more monitors, mixed DPI, and monitor hot-plug. Verify active identity and destination across the entire display layout.
- [ ] Test supported games in borderless and exclusive fullscreen. Manual experimental switching may interrupt rendering; record focus, minimization, and game stability. Do not claim warning delivery, which is outside PR 0.
- [ ] Test held modifier keys and ordinary user vs elevated foreground apps. No shortcut injection is implemented, but input/focus side effects still need observation.
- [ ] Test another Windows build family after ABI review. Unknown configurations must fail before undocumented activation.

## Objective automatic-switch gate

For each configuration claimed as supported:

- [ ] At least 200 eligible, explicitly initiated transitions recorded (100 round trips), with source/target GUIDs, status, total command latency, and manual destination corroboration.
- [ ] Zero wrong destinations, including transient wrong destinations noticed by the tester.
- [ ] At least 99% complete and verify within two seconds end-to-end. For 200 requests this means at least 198; include failures/timeouts, do not count only successes.
- [ ] No closing, minimizing, or relocation performed by probe code; record any shell/game side effects separately.
- [ ] Every timeout/failure stops without retry, corrective transition, or loop.
- [ ] All topology, pinned-window, race, restart, and monitor cases above pass.
- [ ] Identify unsupported cases and safe fallback explicitly. Identification failure means unknown; switching failure means notification-only only where identification remains reliable.

Two ordinary successful transitions do not satisfy this gate. If the shell can switch to an unintended desktop in a deletion/user race despite GUID-object targeting, automatic switching is NO-GO until the approach is changed or a defensible safe constraint is established.
