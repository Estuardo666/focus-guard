# FG-143 — Timer/Cycles switch and cycle selector redesign

## Summary

Implemented the owner-approved Timer/Cycles composition from the supplied references. The switch now has one physically sliding selection surface, the selector presents exactly three built-in rhythms in the approved order, and cycle sessions receive a centered status card plus a continuous focus/break timeline.

## Files changed

- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml`
- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/CyclePresetIcon.cs`
- `src/FocusGuard.Desktop/CyclePhaseTimeline.cs`
- `src/FocusGuard.Desktop/Styles/Controls.axaml`
- `src/FocusGuard.Desktop/Styles/Tokens.axaml`
- `src/FocusGuard.Desktop/Styles/Theme.Dark.axaml`
- `src/FocusGuard.Desktop/Styles/Theme.Light.axaml`
- `src/FocusGuard.Desktop/Resources/UiStrings.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.resx`
- `src/FocusGuard.Desktop/Resources/UiStrings.es.resx`
- `tests/FocusGuard.Desktop.Tests/DesignSystemTests.cs`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- `TASKS.md`

## Tests added

- Structural design-system coverage for the shared sliding surface, theme resources, clipping, Reduce Motion, three-card selector, Coming Soon flyout, centered active card, and timeline bindings.
- View-model coverage for authoritative active phase exposure and the built-in-only selector order when legacy custom data exists.

## Commands executed

- `dotnet format .\FocusGuard.slnx --no-restore`
- `dotnet format .\FocusGuard.slnx --no-restore --verify-no-changes`
- `dotnet build .\FocusGuard.slnx --configuration Release --no-restore`
- `dotnet test .\FocusGuard.slnx --configuration Release --no-build`
- Repeated focused Desktop test runs during visual corrections.
- Native Windows UI Automation selection and actual 125% window captures.

## Results

- Release build: passed with zero warnings and zero errors.
- Final full test suite: 321 passed, zero failed.
- Final Desktop suite: 118 passed, zero failed.
- Timer and Cycles were opened in the real Avalonia application at the owner machine's 125% scale.
- Final visual artifact: `artifacts/ui-qa/2026-08-02/timer-cycle-redesign-final-125pct.png`.
- Updated installer bundle: `artifacts/installer/FocusGuard-0.1.0-cycle-switch/FocusGuard.Setup.exe` and `artifacts/installer/FocusGuard-0.1.0-win-x64-menu.zip`.
- Installer package-contract tests passed and graphical setup smoke test exited with code 0.

## UX implications

- The selected mode slides for 220 ms with an interruptible easing and crossfading text state.
- The profile accent drives switch, selected card, icons, focus outline, and focus segments; breaks use a theme-specific secondary semantic colour.
- Hover adds no elevation; press feedback is a small scale reduction.
- Reduce Motion removes the spatial slide and 30 fps timeline interpolation.
- Timer retains presets and Minutes. Cycles hides Minutes and exposes the unfinished custom-cycle entry as Coming Soon.
- Legacy custom presets remain stored but are intentionally not shown in this three-option reference selector.

## Security implications

- No service, IPC, enforcement, strict-mode, persistence, or recovery behaviour changed.
- The timeline consumes the service-authoritative phase end and remaining time; it cannot activate, extend, or stop a phase.

## Limitations

- A real active cycle could not be safely started against the locally configured protection service during this pass, so the active card and timeline are covered by compiled XAML and status/view-model tests rather than a new live active-session screenshot.
- Orca computer-use was unavailable; Windows UI Automation and native window capture were used as the reproducible fallback.
- The normal `FocusGuard-0.1.0` artifact directory was held open by an unrelated process, so the same contractual version was emitted to the explicit `FocusGuard-0.1.0-cycle-switch` output rather than forcing the lock. A rejected `0.1.1` attempt confirmed the MV3 package-version guard remained effective; no manifest version was changed.

## Next task

Continue FG-145 reboot and controlled recovery validation when the owner schedules a machine-changing drill.
