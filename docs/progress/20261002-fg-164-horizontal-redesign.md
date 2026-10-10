# FG-164 — Horizontal desktop redesign

Owner request: replace the previous vertical composition with a fixed, macOS-inspired
panel interface, using Opal references. Remove the clock dial. Fit 1366 × 768 at 125%.

## Design and implementation plan

Reading this as a focused Windows productivity utility with the quiet, compact panel
language of macOS. Apply apple-design, ui-ux-pro-max, taste, frontend-design and Emil
design engineering. Retain Avalonia and Geist; no landing-page illustrations or imagery
are needed inside this functional application. Database guidance about a marketing
comparison table does not fit this product; use its accessibility and Avalonia guidance.

- Fixed 1008 × 532 DIP client window. At 125%: 1260 × 665 physical pixels, leaving
  103 pixels for native caption, taskbar and margins on the requested display.
- 176 DIP navigation. Remaining space uses two parallel panels: session configuration
  or live status on the left; block list and protection on the right.
- Replace circular clock/glow with left-aligned numeric time and linear progress.
- Keep objective, next break, exact end and primary action in the visible panel.
- Settings, profiles, activity and scheduling share the restrained panel vocabulary;
  long content scrolls inside its own panel. Advanced cycle/strict controls remain usable.
- Preserve current-phase timing, strict review, recovery, schedules, local objectives,
  rule validation and keyboard interactions.
- Add actual Avalonia render coverage for fixed size, 125% output, timer/cycle/break,
  long labels, modal reachability, light/dark and reduced motion.
- Build/test/format, update graph/specifications, package and validate installer 0.1.14.

## Visual system

Geist body 13, headings 20 semibold, timer 60 with tabular columns. Existing semantic
Catppuccin tokens: base #1E1E2E, rail #181825, panels #26263A, text #CDD6F4,
secondary #A6ADC8, accent #CBA6F7. Light theme uses its existing accessible counterparts.
Panel radius 14, gap 16, inset 20. Flat surfaces; no ambient bloom, oversized pills or
decorative looping motion. Profile accent identifies the live progress line.

| Before | After | Why |
| --- | --- | --- |
| 264 DIP rail and tall central form | 176 DIP rail and parallel task panels | More horizontal working space |
| 288 DIP clock dial and glow | Numeric time plus a slim progress line | Keep space for useful status |
| Resizable 1100 × 620 window | Fixed 1008 × 532 DIP | Fits requested 125% display |
| Advanced content pushes the form down | Contained scroll/disclosure | Actions stay reachable |
| Full-surface ambient gradient | Quiet solid panel hierarchy | Content carries the visual emphasis |

## Security and recovery

UI-only redesign. Service remains authoritative. Strict warnings/end/recovery review
stay visible. Closing compact UI does not stop enforcement. No installation or machine
changes are part of this request.

## Status

Implemented and validated, including installer 0.1.14. Preserve all
pre-existing changes; no global formatting, commit or machine installation.

## Implementation and review

Changed MainWindow, FocusWorkspaceView, RuleEditorView, TodayView, ScheduleView,
FocusWorkspaceView code-behind, RuleEditorView code-behind, App styles, navigation
width tokens, new Styles/Workbench.axaml, EN/ES concise copy, and workflow notification.
The numeric timer retains the authoritative current-phase calculation. Its opacity
transition is now 160 ms, removed under reduced motion. Fixed stale session-end copy:
changes to ExactEndText or IsCycleSession now notify SessionEndSummaryText.

Added two native UI tests for settings/strict sheets and long profiles/editor dialogs;
updated layout assertions and timer font expectations for the explicitly requested
design replacement. Existing behavioral tests are retained. Capture uses actual
Avalonia.Headless SetRenderScaling(1.25) before opening the window, not bitmap resizing.
At 125% native frames are exactly 1260 × 665. The quick-start suite also checks 100%.
Frames are in artifacts/ui/fg164, including both themes, active focus/break, profile
search, all six settings sections, strict steps, profile creation and cycle editing.

UI review found and corrected an over-wide profile navigation row, overly narrow
weekday columns, black default focused-field fill, and strict sheet overflow risk.
The seven-day strip now spans the full content width; lists/form scroll below it.
Strict review remains bounded and scrollable for longer closure warnings.

## Security and validation limits

Tests use actual Avalonia/Skia, SQLite and the service with isolated temporary state.
No real blocking session, service install, registry or OS DPI setting was changed.
The window dimensions include an explicit 90 physical pixel allowance for caption
and taskbar on the requested display. Headless DPI rendering verifies layout, not
an installed Windows compositor or assistive-technology session. Native dragging,
real screen placement and screen-reader review remain owner-present checks.

## Commands and results

- Scoped `dotnet format FocusGuard.slnx --no-restore --include ...` and
  `--verify-no-changes`: passed. Exact file list: artifacts/fg164-format-files.json.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: passed with zero warnings
  and zero errors. Log: artifacts/fg164-build.log.
- `dotnet test FocusGuard.slnx -c Release --no-build`: 397 passed, zero failed/skipped.
  Counts: Application 4, Contracts 46, Desktop 158, Desktop.UiTests 13, Domain 34,
  Infrastructure 15, Monitoring 12, NativeMessaging 23, Service 74, Storage 18.
  Log: artifacts/fg164-tests.log; per-project fg164-final.trx receipts retained.
- `npm test`: 19 extension tests passed. `npm run check`: TypeScript passed.
- `graphify update .`: passed, 4,414 nodes, 7,962 edges, 778 communities.
- `scripts/test-installer.ps1`: passed. Chrome/Edge package validation for 0.1.14
  passed, including identity and hashes. Extension source change is version-only.

The old ring geometry assertions were updated for the owner's explicit replacement.
The first full run also caught two expectations for the existing English enforcement
warning; its concise English wording retains "Enforcement is off" and the full rerun
passes. No failed test was skipped. Creation-dialog tests use real control names and
select cycle mode before invoking cycle-only commands.

## Package

`scripts/build-installer.ps1 -Version 0.1.14` passed. Its embedded Desktop hash matched
the freshly published DLL. Standalone `--package-self-test` and `--smoke-test` both
exited 0. Build log: artifacts/fg164-installer-build.log. These checks do not install.

Standalone: artifacts/installer/FocusGuard-0.1.14-Setup.exe
SHA-256: 78E7D60A8536D863B6FCD582595CB7BFB7EB8C7027FBC9D50A56591E8A8D3E4B

Complete ZIP: artifacts/installer/FocusGuard-0.1.14-win-x64.zip
SHA-256: 278FB2208BB3BAA82445F3343A3AB3340EDB92489A3858001ED7D0CC55B6BD61

Published Desktop DLL SHA-256:
AE49D33BA4747DF936BEEC653F10E548F7AB4F4B9B075C50FA89CB278E918C55

## Next task

Owner-present review of the installed window on the physical monitor. No installation
is performed as part of the redesign request.
