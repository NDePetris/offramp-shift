# PR 6 — setup and action feedback

## Scope and evidence boundary

This change improves setup projection, desktop assignment labels, unsuccessful tray
action feedback and first-close preference save handling. It does not change Core
accounting, checkpoint schema, native COM declarations/allowlists, recovery policy,
permissions or collected data. One serialized owner and immutable snapshots remain.

Merged PR #11 completed the accepted PR 5 owner-evaluation scope (owner-reported
Tests 1–7 PASS; Tests 8–12 explicitly deferred / NOT RUN). Merged PR #13 completed
the bounded compatibility investigation and approved exact read-only support for
26200.9457/x64 and 26200.9711/x64. Issues #10/#12 were closed with completion comments
preserving those evidence limits; #8/#9 remain open. Historical validation records
are unchanged. No broad accounting/transition/stability/Explorer campaign is requested.

## Automated regression coverage

- Host: consent/assignment/start progression, unavailable/unsupported/build-mismatch
  and stale states, individual missing desktop status, duplicate/invalid assignment
  rejection with corrective copy, failed provider construction and retained cooldown.
- Host/settings: injected atomic-save failure for both first-close choices preserves
  the old file and active settings; successful save changes only CloseToTray.
- UI: checklist/next-action projection and eligibility, no GUID presentation, stale
  controls, tray command outcomes/deduplication/dismissal/successful retry, failed
  first-close choice remains open, cancellation and saved-choice handling.
- WPF: real control loading/binding, access keys, focusability/tab stops, automation
  button roles/names and polite action-result regions. Explicit result reporting
  raises a UIA live-region event when an automation peer exists.
- Synthetic WPF renders cover Home/Settings, light/dark, setup/monitoring and minimum
  480×420 setup layouts. They contain synthetic data and invoke no live providers.

Automated checks on the PR 6 implementation (October 15, 2026):

| Command/check | Result and evidence |
|---|---|
| `./run.ps1 test` | **PASS** — 67 Core, 65 Persistence, 30 host, 12 app groups; zero build warnings/errors |
| `./run.ps1 format` | **PASS** — all root build and formatting checks |
| `./spikes/VirtualDesktop/run.ps1 test` | **PASS** — 58 fake-provider cases and three owned worker-isolation checks; no switches |
| `./spikes/BrowserAttribution/run.ps1 test` | **PASS** — 2,053 policy/protocol assertions, five mocked extension tests and synthetic native transport checks |
| `./spikes/WindowsObservation/run.ps1 test` | **PASS** — 41 synthetic/local-SQLite tests |
| `./spikes/WindowsObservation/compatibility.ps1 test` | **PASS** — 14 synthetic/owned non-COM groups, unsupported guards retained |
| `./spikes/WindowsObservation/run.ps1 format` | **PASS** |
| App test executable `--render` | **PASS** — 12 synthetic Home/Settings renders; bounded visual review, no live provider calls |
| `git diff --check` and scoped Impeccable detector | **PASS**, detector returned no findings |

Builds retain warnings as errors and locked dependencies. Tests use synthetic providers/local temporary
SQLite; actual owned process checks are non-COM. These do not establish live
Windows UI behavior, native observation reliability or certified accounting.

## Owner acceptance — October 15, 2026

The owner reported **PASS** for the four targeted checks below and accepted PR 6
for its bounded owner-evaluation scope. The implementation at closeout is
`681b03464577da7efd7da0f6ffcca77ef6712f00`; this acceptance update changes only
documentation. The report is owner-supplied evidence, not agent-executed live
verification. It does not supply a separate tested-SHA transcript, exact Windows
build/hardware, display scaling, timings or per-variant results; none are inferred
from earlier runs or from the approved-build list.

| Targeted check | Live result | Evidence boundary |
|---|---|---|
| Home/Settings setup guidance and assignment labels | **PASS — owner-reported** | Existing setup inspected; understandable labels and no raw GUIDs. Complete fresh-account onboarding is not claimed. |
| Duplicate tray assignment rejection | **PASS — owner-reported** | Assigning Work to the existing Personal desktop produced appropriate rejection feedback through **Action needs attention…**, without unexpectedly opening the main window. |
| Close/reopen through the tray | **PASS — owner-reported** | Existing saved close preference was respected. Initial close-choice dialog, newly saving either choice, and preference-save failure were not manually verified. |
| Brief keyboard-focus and Narrator review | **PASS — owner-reported** | Affected controls received a brief review; this is not a complete accessibility or display-variant matrix. |

**NOT RUN / not established by this report:** the complete fresh-account consent →
assignment → Start sequence; the initial close-choice dialog and its choices;
injected preference-save failure; live provider-retry failure; high contrast;
minimum-window-size behavior; and any other unreported theme, DPI, hardware,
notification deduplication, dismissal or successful-retry variant. Deterministic
fault tests and synthetic renders remain separate automated evidence.

PR 5 acceptance Tests 8–12 remain deferred / **NOT RUN**. Explorer recovery (#8),
sustained reliability/accuracy (#9), and existing cleanup limitations remain
unresolved. This acceptance does not approve unattended daily use or certified
accounting and does not lift any safety gate.

## Original short owner smoke procedure — criteria, not a blanket PASS

The procedure below is retained for provenance and possible future checks. Only
the specific observations in the acceptance table above are recorded as PASS;
the broader procedure and unreported variants have not been marked complete.

Record tested commit, Windows edition/build/architecture, display scaling and
PASS/FAIL/NOT RUN for each item. Use an ordinary account on an approved exact build.
Exit the previous app through its tray before launching `./run.ps1 app`. Keep normal
settings/accounting intact; use a separate Windows test account for first-run state.
Do not delete normal data or improvise storage/ACL faults.

1. **Setup:** In fresh test-account state, open Home, then **Review desktop setup**.
   Expect consent incomplete, both roles unassigned, assignment/Start disabled.
   Agree explicitly; select Personal in Task View and assign through the tray.
   Expect Personal assigned/observed and Work incomplete. Assign Work on a different
   desktop; expect Start ready. Select Start; Home's checklist collapses, controls
   remain accessible, Settings retains role statuses/current context, no raw GUIDs.
   Existing configured accounts may inspect completed setup without resetting it.
2. **Tray rejection:** On Personal, attempt **Use current desktop as Work** through
   the tray. Expect rejection explaining distinct desktops and Task View; the main
   window must stay hidden if hidden. Open **Action needs attention…** with the
   Windows tray keyboard controls; verify readable details, Narrator announcement,
   keyboard dismissal and unchanged roles. An advisory balloon may be suppressed
   by Windows; the persistent menu is required. Before dismissing, repeating the
   same rejection should not repeat the balloon. Successful routine actions stay
   quiet; a successful retry of the same command clears its retained error.
3. **First close:** In fresh test-account state, close and choose Cancel: window
   stays open. Close and choose Yes: hide to tray, reopen through tray. Relaunch:
   choice persists. Exercise No (exit, relaunch, retained choice) only if another
   clean test profile is already available; otherwise mark that variant NOT RUN.
   Do not reset normal settings to obtain first-run state. Explicit tray Exit
   remains distinct from hiding.
4. **Accessibility:** On the changed setup/results, use Tab/Shift+Tab, access keys,
   Narrator, one light/dark or system view, high contrast and a small window. Expect
   named controls, visible focus, readable wrapped status/errors and scrolling to
   remaining actions. Record unavailable variants NOT RUN.

Failed close-save and provider-retry failure paths have deterministic fault
coverage; live fault UI acceptance is **NOT RUN** without a reviewed isolated fault
fixture. No normal-data corruption, Explorer restart or broad accounting campaign
is required for this PR. The acceptance table records the bounded live tray feedback
and saved-close-preference checks; other native balloon/dialog and first-close
variants remain NOT RUN unless explicitly reported. Synthetic renders are not
manual passes.

## Unchanged gates

Automatic desktop switching/window movement and production website attribution
remain **NO-GO**. Unattended daily-use acceptance and certified accounting remain
unapproved. Explorer recovery (#8), sustained reliability/accuracy (#9), PR 5
Tests 8–12 and existing blocked-native-call cleanup limitations remain unresolved.
Licensing/public distribution remain undecided. The owner separately authorized
PR #14 merge after final-head CI verification; no deployment or release is authorized.
