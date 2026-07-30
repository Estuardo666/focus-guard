# FG-113 owner-ready checkpoint — blocked on store signing

## Summary

FG-113 is implemented and validated up to the external publisher boundary.
Saved blocked websites and applications are visible and individually removable;
profile edits cannot mutate active/activating policies. The Desktop and extension
are localized in English and Spanish, and rule-card layout was verified from the
installed Release build. Deterministic Chrome/Edge packages, icons, hashes,
native host, per-user registrations, installer rollback, and documentation are
complete.

The checkpoint is not marked done because cryptographic store signatures can be
issued only after the owner submits through Chrome Web Store and Microsoft Edge
Add-ons publisher accounts.

## Files changed

- Desktop Focus/profile rule UI, view models, resource catalogs, and tests
- all secondary Desktop views and simulator copy migrated to resource catalogs
- extension localized manifest/blocked page and deterministic icon assets
- browser packaging, icon generation, validation, and CI scripts
- installer/native-host packaging, registration, rollback, and uninstall logic
- distribution, store publication, progress, and task documentation

## Tests added

- individual persistent application/website rule removal
- active-session editing denial
- English/Spanish critical copy and exact resource-key parity
- Chrome/Edge archive content, icons, secret exclusion, fixed identity, and hashes
- installer contracts for native host and browser distribution

## Commands and results

- full `scripts/ci.ps1`: passed
- Release build: 0 warnings, 0 errors
- .NET tests: 170/170 passed
- extension tests: 13/13 passed; TypeScript passed; npm audit 0 vulnerabilities
- installer/report/reboot/package static checks: passed
- deterministic browser package reproducibility: passed
- installer rollback drill: expected injected failure recovered with 0 tree differences; service running; agent restored
- elevated real installation: service Running/Automatic as LocalService; process agent running
- Chrome and Edge native-host HKCU registrations point to installed manifests
- installed native host exists and accepts only the fixed extension origin
- installed Chrome and Edge ZIP hashes match `distribution.json`
- installed Desktop DLL hash matches the reviewed package
- installed Desktop opened responsive and closed gracefully; visual evidence is `artifacts/fg113-installed-desktop.png`
- graphical `FocusGuard.Setup.exe` opened responsive, displayed install/uninstall
  actions and package status, and closed gracefully without running an action;
  visual evidence is `artifacts/fg113-setup.png`

## UX implications

Rule lists are stacked to keep long executable names readable. Removal is
unambiguous and warns that active policy is unchanged. English/Spanish catalogs
cover the live and secondary views, state/recovery text, and extension blocked
page. The blocked page does not display or receive the attempted query string.

## Security and recovery implications

The installer registers only the exact native-host identity and restores prior
registry values on rollback. Uninstall removes a registration only when it
points inside the FocusGuard installation root. Packages contain no private
keys, credentials, browsing history, test dependencies, or machine state.
Extension loss remains visibly degraded and releases both enforcement paths.

## Blocker

Chrome Web Store and Microsoft Edge Add-ons must apply publisher signatures.
This requires owner-controlled accounts and external submission/review. Codex
must not request, store, or automate publisher credentials. The exact checklist
is `docs/owner-present/fg-113-store-publication.md`.

Local automated browser launch was also rejected by the execution policy; no
new clean-profile claim is made. Existing FG-112 real Chrome evidence remains
valid, while final signed Chrome and Edge clean-profile verification belongs to
the post-store checklist.

## Next task

Owner submits both prepared ZIP files, confirms the assigned identities, waits
for store signatures, and runs the documented signed-package acceptance drill.
After evidence is recorded, mark FG-113 done.

The graphical installer is now included in the distribution root and delegates
to the existing audited install/uninstall scripts. See
`docs/graphical-installer.md` for the package-root contract.

The owner-provided `C:\Users\Stuart\Documents\FocusGuard\icon.png` was copied
to `assets/branding/focusguard-icon.png` without modification and is now the
source for the Setup icon and extension icon sizes.
