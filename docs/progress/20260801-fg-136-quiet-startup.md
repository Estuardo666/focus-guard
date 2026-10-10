# FG-136 — quiet, optional Windows startup checkpoint

## Summary

FocusGuard's per-user ProcessAgent is now published as a Windows GUI-subsystem
executable, so it keeps enforcing in the background without a console window the
user can accidentally close. Windows logon starts the Desktop with an explicit
`--minimized` argument, and the first window hides into the notification area.

Settings now includes a localized, accessible **Start FocusGuard with Windows**
switch backed by the current user's Windows Run key. Installer updates preserve
an existing opt-out, while the ProcessAgent retains its independent startup
registration for strict-session recovery.

## Files changed

- `src/FocusGuard.ProcessAgent/FocusGuard.ProcessAgent.csproj`
- `src/FocusGuard.Desktop/WindowsStartupRegistration.cs`
- `src/FocusGuard.Desktop/Program.cs`
- `src/FocusGuard.Desktop/App.axaml.cs`
- `src/FocusGuard.Desktop/MainWindow.axaml`
- `src/FocusGuard.Desktop/MainWindow.axaml.cs`
- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.cs`
- `src/FocusGuard.Desktop/Resources/UiStrings.resx`
- `src/FocusGuard.Desktop/Resources/UiStrings.es.resx`
- `scripts/install-focusguard.ps1`
- `scripts/uninstall-focusguard.ps1`
- `scripts/test-installer.ps1`
- `tests/FocusGuard.Desktop.Tests/DesktopLifecycleTests.cs`
- `tests/FocusGuard.Desktop.Tests/FocusViewModelTests.cs`
- `tests/FocusGuard.Desktop.Tests/LocalizationTests.cs`
- `tests/FocusGuard.Desktop.Tests/DesignSystemTests.cs`
- `TASKS.md`
- `docs/progress/20260801-fg-136-startup-experience-plan.md`
- `docs/progress/20260801-fg-136-quiet-startup.md`

## Tests added

- minimized startup argument recognition, including case-insensitive input;
- exact quoted Run-key command generation;
- ProcessAgent GUI-subsystem project contract;
- successful and failed startup-setting transitions;
- English and Spanish startup-setting localization;
- installer minimized argument, preference-preserving rollback, and uninstall
  cleanup contracts;
- updated semantic-toggle count for the Settings surface.

## Commands executed

- `dotnet format FocusGuard.slnx --no-restore --verbosity minimal`
- `dotnet build FocusGuard.slnx --configuration Release --no-restore`
- `dotnet test tests/FocusGuard.Desktop.Tests/FocusGuard.Desktop.Tests.csproj --configuration Release --no-restore`
- `dotnet test FocusGuard.slnx --configuration Release --no-build`
- `scripts/test-installer.ps1`
- `scripts/build-installer.ps1 -OutputDirectory artifacts/installer/FocusGuard-0.1.0-quiet-startup`
- direct PE-header inspection of the published ProcessAgent
- `git diff --check`
- `graphify update .`

## Results

- Release build: passed with 0 warnings and 0 errors.
- Desktop tests: 94/94 passed.
- Full .NET suite: 276/276 passed.
- Installer contract tests: passed.
- Installer package: generated under
  `artifacts/installer/FocusGuard-0.1.0-quiet-startup/`.
- Published ProcessAgent PE subsystem: `2`, Windows GUI (no console).
- Diff whitespace validation: passed.

## UX implications

Windows logon no longer presents either the FocusGuard main window or a command
window. The app remains discoverable in the notification area, normal launches
still open it, and the user can opt out from Settings.

## Security implications

The Desktop controls only its own per-user startup registration and remains a
configuration/status client. Process enforcement stays in ProcessAgent; session
authority and finite expiration stay in the Windows Service. No recovery or
security tool is hidden or blocked.

## Recovery implications

Disabling Desktop startup does not disable the independently registered
ProcessAgent. Missing enforcement continues to fail open and report degraded;
install rollback restores both prior Run-key values.

## Limitations

The package was built and inspected but not installed, because this checkpoint
did not authorize changing the current machine installation. The currently
installed build will retain its old startup behavior until the new setup is run.

## Next task

Owner-present installation and a real sign-out/sign-in or reboot verification of
the new quiet startup behavior.
