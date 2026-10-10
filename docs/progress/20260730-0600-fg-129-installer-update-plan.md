# FG-129 — Update installer artifacts

## Summary

Regenerate the self-contained Windows x64 installer from the current validated
source so the packaged Desktop includes Geist, the profile header refinement,
and the profile-colored timer.

## Acceptance criteria

1. Installer PowerShell contracts pass.
2. A fresh `FocusGuard-0.1.0` package and ZIP are generated.
3. The package manifest has all required files.
4. The published Desktop contains the new Geist and profile-gradient markers.
5. Browser package validation passes.
6. The installer itself is not executed; UAC and machine mutation remain owner-present.

## Security and recovery

This checkpoint only rebuilds local artifacts. It does not execute setup,
install a service, modify registry/ACLs, or change the current installation.
The existing installer backup/rollback behavior is packaged unchanged.
