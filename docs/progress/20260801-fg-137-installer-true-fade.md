# FG-137 — Installer true fades, app-parity buttons, and bottom glow

## 1. Summary

Replaced abrupt Setup container swaps with reusable true two-image crossfades,
matched installer button interaction states to the installed Desktop design
system, and added a restrained center-bottom gradient glow to the installer.

## 2. Files changed

- `src/FocusGuard.Setup/Program.cs`
- `src/FocusGuard.Setup/SetupTheme.cs`
- `TASKS.md`
- `docs/progress/20260801-fg-137-installer-true-fade-plan.md`
- generated `artifacts/installer/FocusGuard-0.1.0-true-fade/`
- refreshed `graphify-out/`

## 3. Tests added

The existing Setup smoke path now executes the real asynchronous main-to-success
crossfade with a WinForms message pump before asserting the completion state.

## 4. Commands executed

- `dotnet format src/FocusGuard.Setup/FocusGuard.Setup.csproj --no-restore`
- `dotnet build src/FocusGuard.Setup/FocusGuard.Setup.csproj -c Release -warnaserror`
- `dotnet src/FocusGuard.Setup/bin/Release/net10.0-windows/FocusGuard.Setup.dll --smoke-test`
- `scripts/test-installer.ps1`
- local non-elevated visual screenshot of the Setup DLL
- `graphify update .`
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.0 -OutputDirectory artifacts/installer/FocusGuard-0.1.0-true-fade`
- `git diff --check`

## 5. Results

- Release build: passed with zero warnings and errors.
- Setup smoke test including true crossfade: passed with exit code 0.
- Installer contracts: passed.
- Manual initial-screen visual check: passed; static bottom glow remained subtle
  and buttons rendered with Dark Zen surfaces.
- Motion review: approved; no layout-property animation, bounce, `ease-in`, or
  duration above 300 ms remains.
- Package build: passed.
- `FocusGuard.Setup.exe`: 116,459,704 bytes.
- SHA-256:
  `81A345AB0CC916D14BE7D6747BE3AB1D26A41E885E82DE624602DD06FE51897B`.

## 6. UX implications

- Main/success/recovery screens now use simultaneous outgoing/incoming opacity
  mixing rather than a global dim-and-swap.
- The log container fades on first appearance.
- Buttons reproduce Desktop hover scale 1.015, pressed scale 0.985, pressed
  opacity 0.94, semantic gradient/surface states, hover shadow, and focus ring.
- Setup uses the same `FgEaseHover` (`0.22,1,0.36,1`) and `FgEaseFocus`
  (`0.16,1,0.3,1`) curves as Desktop.
- Windows reduced-motion settings still collapse interpolation immediately.

## 7. Security implications

No privilege, process-closing, script execution, install target, or application
launch boundary changed.

## 8. Limitations

WinForms renders child-control crossfade frames through a double-buffered bitmap
layer rather than a GPU compositor. The transition is rare, bounded to 200 ms,
and verified on the target machine. The package was not run elevated and did not
modify the machine.

## 9. Next task

Owner-present installation and visual acceptance of modal, completion, and
installed-app launch paths using the generated true-fade package.

