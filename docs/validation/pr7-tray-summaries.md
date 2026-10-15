# PR 7 — native tray summaries

The native Windows tray menu shows current context, saved remaining allowance
(or exhaustion), and saved qualifying break progress against the configured
requirement. Break credit comes from saved Core state; it is not described as
Work time. No new timer, monitoring loop, setting, dependency, persisted data or
notification mechanism is introduced.

The existing dashboard refresh projects immutable host snapshots into a small tray
summary. Durations use whole minutes, with `<1m` for positive sub-minute values.
Only changed summary text is applied. The tooltip fits within 63 characters and
includes saved allowance during active monitoring; long combinations prioritize
status and allowance over the product name/context. It never extrapolates from
elapsed UI time. Paused/unavailable/stale/setup states explicitly retain saved
values when available. Unavailable/stale observations show Unknown context; stale
updates use the dashboard's existing two-second freshness boundary. Unacknowledged
or unavailable storage never supplies saved accounting values.

Summaries are disabled, read-only native menu items separated from existing
actions. Pause/Resume, assignment, Retry monitoring, Retry save, Settings, Open,
Exit and retained PR 6 action feedback keep their existing handlers/eligibility.
Detailed storage recovery remains in the existing main window.

## Automated validation

Focused deterministic app tests cover all four contexts, saved allowance and break
values, minute boundaries, no extrapolation or second-level summary churn, pause,
unavailable/stale/owner-failure presentation, incomplete setup, absent state,
pending/blocked/incompatible/recovery storage, exhaustion and completed breaks.
They exercise presentation only; synthetic observations do not validate Windows
shell behavior or accounting accuracy.

Validation commands/results are recorded in the PR. The established application
and probe baselines remain required. No live provider or switching exercises are
part of this change.

## Short owner smoke checklist — NOT RUN

Record tested commit, exact Windows build/architecture and display scaling, then
PASS/FAIL/NOT RUN for these four checks on an already configured owner account:

1. **Personal:** open the tray menu on Personal. Confirm Current context: Personal,
   saved remaining allowance (or exhaustion), and a concise status tooltip. Compare
   with saved usage/configuration in Home/Settings, allowing for minute rounding.
2. **Work:** on the already assigned Work desktop, inspect saved qualifying break
   progress against the configured required duration. It is qualifying break
   credit, not a Work-only timer. No extended run or new transition matrix needed.
3. **Paused/unavailable:** Pause, inspect retained saved values and tooltip, then
   Resume. If unavailable/stale occurs naturally, confirm Unknown context and clear
   status with retained values; otherwise mark that variant NOT RUN. Inspect
   synthetic exceptional-state tests rather than inducing shell/storage faults.
4. **Actions/accessibility:** open the tray with the keyboard and briefly inspect
   summary readability with Narrator; verify navigation skips read-only rows and
   existing actions remain reachable. Open, Settings and Pause/Resume should work;
   inspect assignment/retry eligibility and existing action-failure details when
   applicable. Finish with Exit. Note any clipping or unreadable disabled text.

Live shell/Narrator checks are owner-operated and untested by automation. PR 5
Tests 8–12 remain deferred / NOT RUN; Explorer recovery (#8) and sustained
reliability/accuracy (#9) remain unresolved. Automatic desktop switching and
production browser attribution remain NO-GO. Exact read-only compatibility gates,
privacy boundaries and undecided distribution/licensing are unchanged.
