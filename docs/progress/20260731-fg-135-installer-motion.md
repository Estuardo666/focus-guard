# FG-135 — Zen installer motion and branded completion

## 1. Summary

Added a restrained motion system to Setup, replaced the legacy
blocking-process dialog with a branded owner-sized modal, and added a dedicated
successful-installation state with a progressive check, glow, exit action, and
verified FocusGuard launch action.

## 2. Files changed

- `src/FocusGuard.Setup/Program.cs`
- `src/FocusGuard.Setup/SetupTheme.cs`
- `src/FocusGuard.Setup/BlockingProcessesForm.cs`
- `TASKS.md`
- `docs/progress/20260731-fg-135-installer-motion-plan.md`
- generated `artifacts/installer/FocusGuard-0.1.0-zen-motion/`
- refreshed `graphify-out/`

## 3. Tests added

Expanded Setup's `--smoke-test` contract to instantiate and verify the branded
modal structure and successful-installation controls in addition to the initial
screen transparency/layout assertions.

## 4. Commands executed

- `dotnet format src/FocusGuard.Setup/FocusGuard.Setup.csproj --no-restore`
- `dotnet build src/FocusGuard.Setup/FocusGuard.Setup.csproj -c Release -warnaserror`
- `dotnet src/FocusGuard.Setup/bin/Release/net10.0-windows/FocusGuard.Setup.dll --smoke-test`
- `scripts/test-installer.ps1`
- `graphify update .`
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.0 -OutputDirectory artifacts/installer/FocusGuard-0.1.0-zen-motion`
- `git diff --check`

## 5. Results

- Release build: passed with zero warnings and errors.
- Setup smoke test: passed with exit code 0.
- Installer contract tests: passed.
- Graphify update: passed.
- Installer package build: passed.
- `FocusGuard.Setup.exe`: 116,451,512 bytes.
- SHA-256:
  `EF4A7BF70895E76602D168C65B0031648960862295CEC4290A3E4EB68BC2DB41`.

## 6. UX implications

- Initial Setup and modal fades use the Dark Zen 200 ms standard duration.
- Modal exit uses a faster 140 ms response.
- The owner snapshot is blurred once and remains static under a semantic scrim,
  avoiding animation-time blur cost.
- Success is communicated by icon, heading, body copy, and actions, never by
  color alone.
- Windows client-area animation preferences are honored.

## 7. Security implications

Process discovery and close/kill scope are unchanged. The modal names the exact
blocking applications and warns about unsaved work before the explicit close
action. The launch action validates the expected Program Files executable before
starting it.

## 8. Limitations

The installer was built and smoke-tested but not elevated or used to mutate the
machine. End-to-end installation remains owner-present because Setup requests
administrator privileges. Setup strings still follow the existing installer
architecture rather than the Desktop resource dictionaries.

## 9. Next task

Owner-present visual review and real successful-installation confirmation using
the generated `FocusGuard-0.1.0-zen-motion` package.

