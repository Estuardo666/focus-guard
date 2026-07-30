# Browser extension distribution

Run:

```powershell
.\scripts\build-browser-extension.ps1
.\scripts\test-browser-extension-package.ps1
```

The build writes deterministic Chrome and Edge MV3 ZIP files plus SHA-256 hashes
under `artifacts/browser-extension/<version>/`. The archives contain only the
runtime manifest, scripts, blocked-page UI, and English/Spanish locale files.

The public manifest key fixes the development extension identity to
`emhdinkjpinjnmldipnejnbeefgonjnj`, matching the native-host allowlist. No
private key is stored in this repository or package.

Chrome Web Store and Microsoft Edge Add-ons apply their publisher signature
after an authorized owner uploads the corresponding ZIP. That store operation
requires the owner's publisher accounts and is intentionally not automated or
credentialed by this repository. After store assignment, verify that the store
extension identity matches the native messaging manifests before rollout.

Rollback is non-destructive: disable or remove the submitted store version and
retain the previous approved version. Extension absence makes FocusGuard
degraded and releases website enforcement. A separately acknowledged
process-agent lease continues enforcing application rules until its own lease or
the finite session ends; see ADR 0009.

The complete owner-account submission and post-signing verification checklist
is in `docs/owner-present/fg-113-store-publication.md`.
