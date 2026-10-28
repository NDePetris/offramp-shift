# Issue #26 — timed Pause monitoring

Pause is one committed Core state with an optional absolute UTC deadline. Home
and tray open the same Pause dialog: 15/30 minutes, 1/2 hours, a specific local
date/time, or until manually resumed. Presets capture UTC once at confirmation.
Past custom times are rejected without rolling into tomorrow. Nonexistent DST
times are rejected; repeated times require an explicit first/second occurrence.

The dialog presents four compact quick-duration selections in one row, followed
by mutually exclusive **Until a specific date and time** and **Until I resume**
choices. Selecting an option never submits Pause; the separate **Pause** action
confirms it. Custom input uses a native DatePicker and noneditable hour (1–12),
minute (00–59), and AM/PM selectors, retaining arbitrary minute precision.
The default is the next strictly future quarter-hour, including date/year
rollover and DST transitions. A fall-back ambiguity still requires an explicit
occurrence; user-entered nonexistent or past selections remain rejected.

The local zone label uses a name such as **Local time · Central Time**, without
the Windows display name's fixed standard-time offset. Content determines dialog
height: preset/indefinite modes stay compact, custom fields expand it. Cancel,
Pause and validation remain outside the scroll area. The height cap follows the
current monitor's working area in DIPs on load/DPI changes. Native Fluent controls,
shared theme/contrast resources, access keys and automation names are retained.

Checkpoint payload v4 atomically stores Pause and its deadline. Strict v1/v2/v3
checkpoints migrate with a null deadline; an existing Pause remains indefinite.
SQLite schema/user_version stays 1. Old readers reject v4; there is no downgrade.

The serialized application owner reconciles expiry before observation, on startup,
and after save retry/storage reopen. Expiry depends only on UTC, including while
locked or observations are unavailable. It clears Pause durably, resets estimation,
and requires fresh evidence before accounting. Sleep and app exit do not move the
deadline. There is no downtime usage, break credit, reminder replay, countdown,
Windows startup registration, timer service or network component.

Failed or uncertain saves stop accounting and retain exact pending-payload retry.
An expired confirmed Pause with unavailable persistence has distinct actionable
feedback; no successful resumption is inferred from time passing. Manual Resume
uses the same acknowledgment boundary and does not require healthy observations.
Ending Pause does not enable monitoring or bypass setup/observation requirements.

UTC wall-clock corrections can change when the deadline is reached. Forward jumps
can end Pause early in elapsed-time terms; backward jumps can delay it. A durably
ended Pause is never resurrected. Existing future-SavedUtc clock blocking remains
in force. Local timezone changes affect presentation only. Owner-loop delays and
suspension can delay execution until the next available turn; this is not a
real-time scheduling guarantee.

## Automated validation

All seven required commands passed with SDK **10.0.401** and warnings as errors:

| Command | Automated result |
|---|---|
| `./run.ps1 test` | PASS: Core 86 / 40,623 assertions; Persistence 108 / 970; Host 338 / 3,272; WPF 162 / 195; zero failures. |
| `./run.ps1 format` | PASS: verify-no-changes. |
| `./spikes/VirtualDesktop/run.ps1 test` | PASS: 58 fake-policy cases and owned non-COM isolation checks. |
| `./spikes/BrowserAttribution/run.ps1 test` | PASS: 2,053 policy/protocol assertions plus extension/transport tests. No real browser attribution matrix. |
| `./spikes/WindowsObservation/run.ps1 test` | PASS: 41 tests / 10,300 assertions; synthetic observation and disposable SQLite. |
| `./spikes/WindowsObservation/compatibility.ps1 test` | PASS: 24 unsupported-entry guard cases and 16 synthetic/owned-process diagnostic groups. No live COM activation. |
| `./spikes/WindowsObservation/run.ps1 format` | PASS: verify-no-changes. |

`git diff --check` passed. Local ignored logs are `artifacts/issue26-*.log`.
The application test/format commands used environment-only
`UseArtifactsOutput=true` and `ArtifactsPath=<repo>/artifacts/issue26-build`,
because the owner's running app locked the ordinary output directory. The
running app was not closed or changed; build scripts were not modified.

Offscreen synthetic WPF renders covered light/dark Home, preset/custom dialog,
and minimum-size custom scrolling. A separate UI review verified the existing
scroll gutter and legible custom controls/actions. These are layout/source checks,
not owner acceptance or live accessibility results.

The dialog redesign adds WPF regressions for exclusive radio selection without
submission, confirmation/cancellation, all minute options, noon/midnight, quarter-
hour/date/year/DST rollover, invalid and ambiguous custom times, accessible
control properties, natural expansion/collapse, and minimum-size scrolling.
The eight light/dark preset/custom/start/end captures were independently reviewed
with no material layout findings. Native chrome/popups, physical keyboard,
screen readers, high contrast and mixed-DPI transitions remain **NOT RUN** as
owner acceptance. Redesign logs use `artifacts/issue26-redesign-*.log`.

Tests use fake clocks/providers and disposable SQLite,
including strict migration, UTC boundaries, replacement/manual resume, restarts,
clock corrections, lock/suspend/unavailable observation, atomic save faults,
acknowledgment loss/exact retry, no retroactive credit, conversion and UI controls.
They do not establish live Windows shell reliability or accessibility.

## Focused Pause-selection race correction

The fake UTC clock crosses the proposed deadline between host validation and
Core's durable transition check. For active, indefinite-Pause and timed-Pause
starting states, the command is rejected, persistence stays Ready with no failure,
and the full confirmed state/configuration and SQLite bytes are unchanged.
Direct invalid selections are covered too. A reused selection exception during
checkpoint capture still blocks a partial transition; invalid monotonic clocks
still block storage. Existing save-fault/uncertain-commit and exact-retry tests
remain passing. Only the explicit pre-mutation Pause rejection is exempted.
Correction logs use the ignored `artifacts/issue26-correction-*.log` prefix.

## Minimal Windows owner smoke — NOT RUN

Implementation merged in PR #29 on October 28, 2026. The owner authorized merge
without additional live Pause testing as a prerequisite; issue #26 remains open
pending this acceptance. The separate successful PR #29/#30 authorization capture
does not complete these Pause checks or relabel historical NOT RUN results.

Record the tested commit, exact approved Windows build/x64, and display scaling.

1. Open Pause from Home. Choose a custom local end time a few minutes ahead.
   Confirm Home and tray show the same saved resume time.
2. Observe the automatic transition after that time. Require Pause to end and
   the saved status to update; accounting may wait for fresh healthy evidence.
3. Pause again, choose **Resume now** through the tray, and confirm Home agrees.

Record PASS/FAIL/BLOCKED/NOT RUN for these three steps. No extended Windows
validation campaign is required. No owner results are claimed by automated tests.

Compatibility restrictions are unchanged. Automatic desktop switching and
production browser attribution remain **NO-GO**. PR 5 Tests 8–12 remain deferred /
NOT RUN; this feature does not certify accounting or unattended daily use.
