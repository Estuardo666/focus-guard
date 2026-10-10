# FG installer refresh — current UI bundle

## Summary

Regenerated the Windows x64 installer bundle from the current Release sources so the
distributed Desktop binary includes the owner-reference clock dial and coordinated timer
copy transition. The graphical setup keeps its existing Windows-native title bar, elevation,
rollback, uninstall, and recovery flows.

## Artifacts

- `artifacts/installer/FocusGuard-0.1.0/FocusGuard.Setup.exe`
- `artifacts/installer/FocusGuard-0.1.0-win-x64.zip`
- Updated publish folders for Desktop, Service, Recovery, ProcessAgent, NativeMessaging,
  browser extension, scripts, and manifests under the same package root.

## Commands and results

- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.0` — passed.
- `scripts/test-installer.ps1 -PackageDirectory artifacts/installer/FocusGuard-0.1.0` — passed.
- `FocusGuard.Setup.exe --smoke-test` — exit code 0.

## Limitations

This refreshed package was smoke-tested and statically validated, but the installer was not
run through a real elevated install/update/uninstall mutation in this pass. The existing
owner-present installation drill remains the required next step for machine-changing
verification.
