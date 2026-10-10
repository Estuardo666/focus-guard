# FG-165 — Session home and monitor-aware fixed sizing

The owner rejected FG-164 as too similar to the original and too small at 1920 × 1080,
100%. Re-inspected all five supplied images. Opal's defining desktop composition is
a narrow action rail, a list of wide session rows grouped by current/upcoming, small
inline app/status summaries, and deliberate configuration on demand. The prior tall
timer/form plus mostly empty block column missed that composition.

## Plan

1. Replace Focus home with Now, Upcoming and profile session rows. Keep real service
   state and actual schedules; no invented analytics. Configuration opens as a bounded
   sheet with its primary action fixed. Blocks become a contextual flyout.
2. Make the application shell graphite with a soft mint action accent; preserve semantic
   warning colors and Geist. Simplify sidebar to destinations plus New session.
3. Keep manual resizing/maximizing disabled, but calculate client size from the monitor's
   usable physical area and scale. Target 1440 × 840 DIP on 1920 × 1080 at 100%; fit
   1044 × 528 DIP on a 1366 × 720 working area at 125%, allowing native frame/taskbar/margins.
4. Preserve timer/cycle/strict activation and review, compact window, rule editing,
   history, real scheduling, keyboard and reduced-motion behaviors.
5. Render and test both monitor cases, interaction with configuration, strict review,
   active/break state and missing/long data. Format/build/full suites/graph. Package
   standalone 0.1.15, validate payload and smoke without installation.

## Design review

| Before | After | Why |
| --- | --- | --- |
| Tall timer and a separate sparse block column | Wide session row with compact status, time and progress | Matches reference information hierarchy |
| Full setup form on the landing screen | Quick start row and explicit configuration sheet | Starting and configuring are distinct tasks |
| Profile names dominate navigation | Profiles live in the session library | Navigation stays short and predictable |
| Same purple-tinted surfaces | Neutral graphite layers with mint actions | Changes the visual identity, not only spacing |
| One fixed size for all monitors | Fixed size selected per usable monitor area | Larger on 1080p while respecting 125% small displays |

## Boundaries

UI remains status/configuration only. No service enforcement or persistence changes.
Exact end, blocking opt-in, strict recovery/confirmation and degraded warnings stay
visible. New/quick sessions use the existing validated activation flow. No installation,
OS display setting change, external account or downloaded image assets.

## Status

Completed and validated with standalone installer 0.1.15. E: was initially disconnected; owner reconnected it. Existing working
tree changes remain intact. Continue applying previously requested design skills.

## Implementation and visual review

New SessionHomeViewModel.cs and DesktopWindowLayout.cs. Changed FocusWorkspaceView
and its code-behind, MainWindow/screen sizing/keyboard/overview refresh, FocusViewModel
profile-load notification, FocusWorkflowViewModel schedule preview/availability,
RuleEditorViewModel UI notifications, Workbench styles, dark/light theme action tokens,
body/caption/navigation tokens and EN/ES resources. Added no enforcement behavior.

Session configuration disables the home and navigation, returns focus on dismissal,
and closes when strict review or activation begins. Home profiles refresh after actual
store loads rather than on every selection, preserving context-menu targets. Overview
data refreshes on opening, returning to Sessions and session closure. Unavailable
schedule reads show their limitation while the authoritative timer remains active.

The actual Avalonia renders show 1440 × 840 client content at 100% and 1305 × 660 pixels
at 125% for the smaller working area. Inspected home, active/break, setup, profile
editor, weekly schedule, activity light/dark, settings and strict review. Test library
profiles and the upcoming schedule are isolated fixture data, not seeded in the product.
Graphite/mint matches the desktop references' neutral surfaces and soft action accent;
there is no tall always-open form or permanent sparse block column.

## Tests and remaining validation

Updated native UI coverage checks monitor sizing, fixed constraints, quick start,
opening/dismissing the setup sheet, profile selection, navigation lock and fresh strict
review. Added an overview read failure/recovery test proving active session state is
unchanged. Geometry/palette/source assertions now check the requested design, while
contrast, keyboard, actual IPC/storage, service and recovery tests remain intact.

Final formatting, full suites, graph refresh and installer checks passed. No
machine installation or OS display settings changed. Headless renders verify DPI
layout; physical Windows placement, screen-reader and installed-app interaction remain
owner-present checks.

## Validation results

- Scoped `dotnet format` and `--verify-no-changes` passed. The exact C# list is saved
  in artifacts/fg165-format-files.json. New FocusWorkspace/Workbench XAML is indented;
  changed text uses CRLF. Existing unrelated working-tree edits are preserved.
- `dotnet build FocusGuard.slnx -c Release --no-restore --no-incremental` passed with
  zero warnings/errors. Full compilation was repeated after an external-volume reset.
  Final log: artifacts/fg165-build.log.
- `dotnet test FocusGuard.slnx -c Release --no-build` passed: 398 tests, zero failed
  or skipped. Counts: Application 4, Contracts 46, Desktop 158, Desktop.UiTests 14,
  Domain 34, Infrastructure 15, Monitoring 12, NativeMessaging 23, Service 74, Storage 18.
  Log: artifacts/fg165-tests.log and per-project fg165-final.trx.
- Native UI tests cover 1920 × 1040 working area at 100% (1440 × 840 client),
  1366 × 720 at 125% (1044 × 528), and 1920 × 1040 at 125% (1440 × 784), plus
  timer/cycle/break, setup navigation lock/Escape, profile selection, strict review,
  missing overview data, real SQLite/service data, both themes and long names.
- `npm test`: all 19 passed. One concurrent run measured a navigation median of
  13.649 ms while the full compiler/formatter were running; an isolated rerun passed
  the unchanged 10 ms budget. No thresholds were relaxed. `npm run check` passed.
- `graphify update .` passed: 4,464 nodes, 8,041 edges, 799 communities.
- Installer script and Chrome/Edge 0.1.15 package tests passed. Extension change for
  this task is manifest version only. Standalone build and final checks passed.

## Environment and storage

The external E: volume reset during compilation, producing AVLN9999 (opened volume
externally altered) and missing-path build errors. After it reappeared, a complete
fresh compilation and the full suite passed. A temporary source snapshot initially
completed in C:; the owner requested avoiding C:. Automatic review blocked its deletion
without further detail. A checked, native PowerShell move preserved it under
D:/TEMP/focusguard-fg165-validation and removed it from C:. Final validation remains
in the original E: workspace, with TEMP/TMP and bundle extraction redirected to D:.
No disk permissions, OS display settings, services or installed application state changed.

## Installer artifacts

`scripts/build-installer.ps1 -Version 0.1.15` passed. Its freshness gate confirmed that
the standalone contains the newly published Desktop DLL. Standalone --package-self-test
and --smoke-test both exited 0. Temporary extraction used D:/TEMP. Build log:
artifacts/fg165-installer-build.log. No installation was performed.

Standalone: artifacts/installer/FocusGuard-0.1.15-Setup.exe
SHA-256: FD2E1F4108E2E3D92BDC0E1ECC9AED833E7C6EB04550DE40CE5A1BE4A5511140

Complete ZIP: artifacts/installer/FocusGuard-0.1.15-win-x64.zip
SHA-256: 35DAEB77853753A6E1699A36A1C705A88483BAEA6F0CAEFF1C3816E7E3BC6844

Published Desktop DLL SHA-256:
12409BEA9B91C4AE6AFBA7EDC7954BA8937D888FBF00CD34484E50F7169F13A3

## Next task

Owner-authorized installation and physical Windows placement/accessibility review.
Headless layouts are verified; installed compositor/assistive-technology interaction
remains an owner-present follow-up. Existing recovery/install milestones are preserved.
