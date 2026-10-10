# FG-143 checkpoint — owner-reference clock dial correction

## Summary

Replaced the layered generic progress ring with a purpose-built clock dial matching the
owner references: a full chromatic ring in both states, sixty clock indexes with longer
five-minute marks, an inactive state with no progress head, and an active-only clockwise
elapsed-time overlay with a subtle light gradient and luminous head. The previously approved
209 DIP diameter and localized center labels remain unchanged.

## Files changed

- `src/FocusGuard.Desktop/ClockDial.cs`
- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml`
- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml.cs`
- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/Styles/Tokens.axaml`
- `src/FocusGuard.Desktop/Styles/Controls.axaml`
- `src/FocusGuard.Desktop/Styles/Theme.Dark.axaml`
- `src/FocusGuard.Desktop/Styles/Theme.Light.axaml`
- `tests/FocusGuard.Desktop.Tests/DesignSystemTests.cs`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- `TASKS.md`

## Tests added

- Elapsed-time ring-angle cases for inactive, start, quarter, half, and complete states.
- XAML contract coverage for the clock dial's active state and semantic dynamic brushes.

## Commands executed

- `dotnet format .\FocusGuard.slnx --no-restore`
- `dotnet format .\FocusGuard.slnx --no-restore --verify-no-changes`
- `dotnet build .\FocusGuard.slnx --configuration Release --no-restore`
- `dotnet test .\FocusGuard.slnx --configuration Release --no-build`
- `graphify update .`

## Results

- Release build: passed with 0 warnings and 0 errors.
- Full suite: 318 passed, 0 failed, 0 skipped.
- Graph: 3234 nodes, 5657 edges, 442 communities.
- OFF state visually reviewed at Windows 125% scale:
  `artifacts/ui-qa/2026-08-02/ring-clock-off-125pct.png`.

## UX implications

The ring now reads as a clock rather than a generic progress control. OFF has no progress
indicator. ON uses elapsed time clockwise from twelve. Center-copy changes remain a 220 ms
opacity-only crossfade and switch immediately when Reduce Motion is enabled.

## Security implications

None. Session enforcement, IPC, expiry, persistence, and recovery behavior are unchanged.

## Recovery implications

Presentation-only. The old XAML ellipse/arc composition can be restored without changing
session data or service state.

## Limitations

The installed service rejected a normal, non-strict, enforcement-disabled activation during
this run, so an honest live ON screenshot could not be captured. The ON geometry and elapsed
angle are covered by automated tests, but still require live visual confirmation once the
service activation path is available. No strict session or blocking policy was left active.

## Next task

Resolve or provide a working local service activation path, then capture and compare the live
ON countdown and its consecutive center-copy crossfades.
