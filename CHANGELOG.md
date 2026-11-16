# Changelog

## 1.7.0 — 2026-11-16

- Added ordered multi-point clicking to the Windows Mouse clicker page, with numbered circles, Arrange points and F11 append.
- Added an over-target editor for placing, dragging, reordering and removing up to 100 points. Save and Cancel leave playback stopped; target changes close the editor for review.
- Point routes complete each point's burst before moving to the next, retain unfinished progress across focus pauses, and obey global click/time limits.
- Added deep point snapshots and version-2 route profiles; earlier profiles remain readable, and older apps reject new route files.
- Added route model, scheduling, persistence and small/scaled interface checks. Android remains the 1.6.0 preview.

## 1.6.0 — 2026-11-16

- Added Android straight-swipe actions alongside taps, with endpoint mapping and per-action duration.
- Added floating controls to add tap/swipe actions and remove the last action while stopped.
- Preserved serialized gesture callbacks, target checks and explicit Stop behavior. Swipe paths are checked for covering windows.
- Added backward-compatible tap profiles and version-2 swipe profiles; imports clear all points.
- Added Windows profile search with visible match counts and Escape to clear.
- Public examples and release packaging now contain only general starter presets. Personal setups remain local.
- Added privacy documentation and a researched Google Play publishing and one-time Pro plan. No billing or store enrollment added.

## 1.5.0 — 2026-11-16

- Browse included presets directly from the Profiles page, inspect a setup, and add it to the saved library without starting playback.
- Open the bundled preset folder or the saved profile directory from the app.
- Added general starter presets.
- Improved build output handling, public setup documentation and reproducible release packaging.
- Clarified that F9 starts/stops a session. Automatic focus pauses preserve optional cooldown deadlines; explicit Stop/Start begins fresh session timing.
- Added GitHub Actions checks for Windows tests, compilation and interface previews, plus Android scheduler tests.
- Published source and downloadable Windows/Android artifacts separately. Android remains the 1.4.0 preview in this release.

## 1.4.1

- Added optional cooldown-preserving timer rows for single presses. Focus and readiness pauses retain the last accepted press's deadline.
- Preserved independent timers that leave the pointer where the user aims it.
- Preserved cooldown settings through profile loading, duplication and portable transfer.

## 1.4.0

- Introduced the Flow Macro branding, icon and five-page Windows workspace.
- Added a dedicated mouse clicker with bursts, fixed/current points, exact click totals and timed or infinite sessions.
- Expanded supported keyboard inputs and retained compatibility with earlier profiles.
- Generalized Android's setup, tap pad and public branding while retaining its package and mobile profile format.

## Earlier development

- Added independent timers, ordered recording/replay, named profiles and portable JSON transfer.
- Added target-window focus checks, input diagnostics, permission matching and emergency release behavior.
- Built the Android accessibility tap-runner preview and Windows-to-Android timer export.
