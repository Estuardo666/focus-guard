# FG-161 checkpoint — stale break navigation race

## Summary

The browser enforcer now revalidates a blocking decision against the latest
stored, integrity-checked policy immediately before redirecting. A focus-era
async callback can no longer fight the block page after a cycle enters an
authorized break.

## Files changed

- `extension/src/navigation-enforcement.js`
- `extension/test/navigation-enforcement.test.ts`
- `extension/manifest.json`
- `TASKS.md`
- FG-161 plan/checkpoint and generated graph files

## Tests added

- A navigation first evaluated under focus policy changes to an empty break
  rule set before redirect; the stale callback must not open the block page.

## Commands executed

- `npm test -- --test-name-pattern "stale focus evaluation"` before and after
  the fix
- `npm test`
- `npm run check`
- `dotnet build FocusGuard.slnx --no-restore`
- `dotnet test FocusGuard.slnx --no-build --no-restore`
- `scripts/build-installer.ps1 -Version 0.1.11`
- `scripts/test-installer.ps1`
- `scripts/test-browser-extension-package.ps1 -Version 0.1.11`
- `graphify update .`

## Results

- The new regression failed before the fix because only one policy read was
  made, then passed with the latest-policy revalidation.
- Extension: 19 passed, zero failed; TypeScript check passed.
- Build: passed with zero warnings and zero errors.
- .NET: 360 passed, zero failed.
- Installer and Chrome/Edge package validation passed.
- Standalone installer: `FocusGuard-0.1.11-Setup.exe`, SHA-256
  `68C0A10C4989263B1CE2189E8F00224AD7184DAEFC4F74251E82B60010BAB5A7`.
- Complete ZIP: `FocusGuard-0.1.11-win-x64.zip`, SHA-256
  `FE549F0F102D3968DC749EBFC6683BFFDEACF5A43DC92EA796440FA5F2B7B5D2`.

## UX implications

Blocked URLs become reachable during an authorized break without repeated
block-page flashes. URLs still blocked by a configured break exception retain
the normal block experience.

## Security implications

The second check grants no authority. It uses the same confirmed policy and
integrity verification as the first check, and only cancels a redirect when
the newest valid state allows navigation.

## Limitations

The race is reproduced and verified deterministically in the extension test
harness. The new package was not installed because installation changes the
machine and requires explicit owner authorization.

`git diff --check` also reports pre-existing whitespace in
`src/FocusGuard.Desktop/MainWindow.axaml`; FG-161 did not modify that file.

## Next task

Run an owner-present installed Chrome/Edge focus-to-break navigation drill for
0.1.11 as part of FG-145 recovery and installer validation.
