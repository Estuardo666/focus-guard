# FG-130 installer update checkpoint

## Summary

Rebuilt the Release `win-x64` installer package from the FG-130 sources. The
package contains the updated Desktop single-instance behavior, adaptive
refresh, pooled process inspection, compiled application-rule matcher, and
ProcessAgent heartbeat changes.

## Artifacts

- `artifacts/installer/FocusGuard-0.1.0-win-x64.zip` (280,423,686 bytes)
- `artifacts/installer/FocusGuard-0.1.0/FocusGuard.Setup.exe` (116,164,792 bytes)
- `artifacts/installer/FocusGuard-0.1.0/manifest.json` (version `0.1.0`, 14 files)
- self-contained Service, Recovery, Desktop, ProcessAgent, and NativeMessaging
  publish directories
- Chrome and Edge MV3 packages and native-messaging manifests

## Commands and results

- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.0`: passed
- `scripts/test-installer.ps1`: passed
- `scripts/test-browser-extension-package.ps1`: passed

## Security and recovery

The package uses the existing service account, native-host origin validation,
recovery binary, and installer rollback logic. No service registration,
registry value, ACL, browser policy, or running process was changed.

## Limitation and next step

The installed legacy binaries remain active until an owner-present installation
is explicitly authorized. The next safe verification is to run the installer
update/rollback drill with the exact package directory and then rerun the
resource canary after installation.
