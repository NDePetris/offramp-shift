# Issue #26 — timed Pause monitoring

Pause is one committed Core state with an optional absolute UTC deadline. Home
and tray open the same Pause dialog: 15/30 minutes, 1/2 hours, a specific local
date/time, or until manually resumed. Presets capture UTC once at confirmation.
Past custom times are rejected without rolling into tomorrow. Nonexistent DST
times are rejected; repeated times require an explicit first/second occurrence.

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
| `./run.ps1 test` | PASS: Core 86 / 40,623 assertions; Persistence 105 / 940; Host 335 / 3,258; WPF 158 / 195; zero failures. |
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

Tests use fake clocks/providers and disposable SQLite,
including strict migration, UTC boundaries, replacement/manual resume, restarts,
clock corrections, lock/suspend/unavailable observation, atomic save faults,
acknowledgment loss/exact retry, no retroactive credit, conversion and UI controls.
They do not establish live Windows shell reliability or accessibility.

## Minimal Windows owner smoke — NOT RUN

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
