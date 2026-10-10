# FG-133 — Installer startup and transparent-control fix

## 1. Summary

Fixed the redesigned setup window closing immediately and removed the white
rectangles behind the custom shield and administrator warning.

## 2. Root cause

`ShieldLockControl` and `IconTextControl` assigned a transparent background
before enabling WinForms transparent-background support. Their constructors
threw `ArgumentException`, terminating setup before the message loop began.
The default opaque control backgrounds produced the white rectangles in the
earlier visual build.

## 3. Files changed

- `src/FocusGuard.Setup/Program.cs`
- `src/FocusGuard.Setup/SetupTheme.cs`
- this checkpoint
- regenerated `graphify-out/`
- regenerated `artifacts/installer/FocusGuard-0.1.0-redesign/FocusGuard.Setup.exe`

## 4. Tests added

Added a `--smoke-test` startup path that constructs and shows the real setup
form, performs layout, and asserts that the title bar, footer, shield, warning,
and transparent backgrounds are present.

## 5. Commands and results

- Reproduced with `dotnet FocusGuard.Setup.dll`: failed with
  `Control does not support transparent background colors`.
- Release build with warnings as errors: passed, 0 warnings and 0 errors.
- `FocusGuard.Setup.dll --smoke-test`: passed with exit code 0.
- Installer script contracts: passed.
- Non-mutating visual launch: passed; no white control backgrounds.
- Corrected setup SHA-256:
  `753E9D19C375D3A51F695ED289CEF2E2E444B084252789DE832520113514C6C5`.

## 6. UX implications

Setup now remains open and its two custom illustrated controls blend into the
Dark Zen surface. Explicit root rows preserve the title, body, operation log,
and help footer at different DPI settings.

## 7. Security and recovery implications

The verification did not click install or uninstall and made no service,
registry, ACL, browser-policy, or recovery changes. The UAC manifest remains
unchanged.

## 8. Limitations

The full-screen capture API is DPI-virtualized in this environment, so a
915-logical-pixel capture at 125% scaling crops the right and bottom physical
pixels. Runtime layout inspection confirmed those controls are visible.

## 9. Next task

Owner launch verification of the corrected single-file executable.
