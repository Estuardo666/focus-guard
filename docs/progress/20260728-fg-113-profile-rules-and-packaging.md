# FG-113 checkpoint — profile rule management and browser packages

## Summary

The selected profile now shows its saved website and application rules in the
real Focus screen. Each rule can be removed individually and the revised
profile is persisted. Editing is denied while service state is `Activating` or
`Active`, so the immutable acknowledged policy is never changed mid-session.

Chrome and Edge receive deterministic MV3 ZIP packages with English and Spanish
extension metadata and a localized, privacy-preserving blocked page. Store
publisher signing remains an owner-account operation.

## Files changed

- Desktop profile view model, Focus XAML, and Desktop tests
- extension manifest, blocked page, and `en`/`es` locale catalogs
- deterministic browser package build/test scripts and CI integration
- FG-113 plan, distribution documentation, task queue, and this checkpoint

## Tests added

- Persistent removal of one application rule without affecting website rules
- Persistent removal of one website rule without affecting application rules
- Active-session removal denial and command disabling
- Chrome/Edge archive contents, fixed identity, secret exclusion, and SHA-256 verification

## Commands and results

- `dotnet format FocusGuard.slnx --no-restore`: passed
- `scripts/ci.ps1`: passed; build has 0 warnings/errors, 167/167 .NET tests pass
- extension TypeScript check and 13/13 tests: passed; npm audit reports 0 vulnerabilities
- installer, reboot-drill, report, and browser-package validations: passed
- development Desktop binary launched from the Release output: responsive window confirmed
- `orca computer`: unavailable because the documented CLI is not installed; pixel/accessibility inspection was not claimed

## UX implications

Users can see exact saved blocked targets instead of only counts and can remove
one without rebuilding the profile. Copy explains that changes affect future
sessions only. The extension-owned blocked page no longer exposes native-host
diagnostic JSON and never receives the attempted URL as a query parameter.

## Security and recovery implications

Profile edits remain separate from active immutable policies. Query strings are
still rejected. Packages omit tests, dependencies, machine state, private keys,
and credentials. Missing extensions remain fail-open and release both paths.

## Limitations

- Store submission/signing requires the owner's Chrome Web Store and Edge Add-ons accounts.
- Desktop-wide Spanish localization and final visual accessibility inspection remain in FG-113.
- The locally launched development Desktop was closed without activating enforcement.

## Next task

Finish Desktop-wide resource extraction/Spanish localization, perform the final
visual accessibility pass with an available UI inspection tool, then run the
owner store-upload checklist.

## Localization and visual follow-up

- Added English and Spanish (`es`) Desktop resource catalogs for the live Focus
  screen, blocked-rule lists, finite-session warning, strict confirmation, and
  recovery copy.
- Added culture-switch tests for Spanish critical UI and English fallback.
- Captured the real Release window at `artifacts/fg113-desktop.png`. The first
  pass exposed cramped side-by-side rule cards; the cards are now stacked so
  long executable names retain readable width and each Remove action remains
  unambiguous.
- Re-ran the full gate after the visual fix: 169/169 .NET tests, 13/13 extension
  tests, 0 warnings/errors, and all packaging/recovery checks passed.
