# FG-134 — Profile-coloured tray flyout

## 1. Summary

Added a custom, localized system-tray flyout inspired by the owner reference.
Left-clicking the FocusGuard tray icon now toggles a compact status surface that
shows the shared authoritative Desktop session state, selected profile, remaining
time, and the selected profile's website and application rule counts.

The remaining time and status-row icons use the profile accent. The flyout
supports Dark Zen and White Zen through semantic resources, is excluded from the
taskbar, stays inside the primary screen work area, and closes on deactivation or
Escape.

## 2. Files changed

- `src/FocusGuard.Desktop/TrayFlyoutWindow.axaml`
- `src/FocusGuard.Desktop/TrayFlyoutWindow.axaml.cs`
- `src/FocusGuard.Desktop/App.axaml.cs`
- `src/FocusGuard.Desktop/MainWindow.axaml.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.resx`
- `src/FocusGuard.Desktop/Resources/UiStrings.es.resx`
- `tests/FocusGuard.Desktop.Tests/DesignSystemTests.cs`
- `tests/FocusGuard.Desktop.Tests/DesktopLifecycleTests.cs`
- `tests/FocusGuard.Desktop.Tests/LocalizationTests.cs`
- `TASKS.md`
- `docs/progress/20260731-fg-134-tray-flyout-plan.md`
- `docs/progress/20260731-fg-134-tray-flyout.md`

## 3. Tests added

- Bottom-right work-area placement.
- Flyout profile-accent binding, Settings action, and taskbar exclusion.
- Spanish tray-session and inactive-state copy.

## 4. Commands executed

- `dotnet format FocusGuard.slnx --no-restore`
- `dotnet format FocusGuard.slnx --no-restore --verify-no-changes`
- `dotnet build FocusGuard.slnx --no-restore`
- `dotnet test FocusGuard.slnx --no-build --no-restore`
- `graphify update .`

## 5. Results

- Formatting: passed.
- Build: passed with 0 warnings and 0 errors.
- Tests: 269 passed, 0 failed.

## 6. UX implications

The tray icon is now a glanceable status entry point instead of immediately
opening the full shell. Open FocusGuard and Settings restore the existing window
to the expected destination. The native menu remains available as a
platform-standard fallback.

The flyout explicitly distinguishes an inactive session and keeps the selected
profile visible. Strict mode retains a visible warning and exact release copy.

## 7. Security implications

The flyout does not enforce, stop, shorten, or mutate sessions. It reads the same
view model as the main Desktop window. Exit closes the Desktop application but
does not alter the service-owned finite session.

## 8. Limitations

Avalonia's tray click API does not expose the tray icon rectangle, so the flyout
anchors to the lower-right edge of the primary screen work area rather than to an
exact icon pixel. This remains correct for standard Windows taskbar placement and
keeps the entire flyout on screen.

## 9. Next task

Rebuild the graphical installer so packaged Desktop binaries include FG-134,
after owner visual review of the flyout on the target Windows scaling setting.
