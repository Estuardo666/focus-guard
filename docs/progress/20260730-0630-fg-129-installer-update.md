# FG-129 — Installer artifacts updated

## 1. Summary

Regenerated the default self-contained Windows x64 installer from the current
FG-128 source. The package now contains the bundled Geist Desktop resources and
the updated profile header/timer implementation.

## 2. Files changed

- `TASKS.md`
- `docs/progress/20260730-0600-fg-129-installer-update-plan.md`
- this checkpoint
- `artifacts/installer/FocusGuard-0.1.0/`
- `artifacts/installer/FocusGuard-0.1.0-win-x64.zip`
- regenerated `artifacts/browser-extension/0.1.0/`

## 3. Tests and commands

- `scripts/test-installer.ps1`
- `scripts/build-installer.ps1`
- `scripts/test-browser-extension-package.ps1`
- manifest file existence and packaged Desktop marker checks
- SHA-256 verification of Setup, ZIP, and Desktop DLL

## 4. Results

- Installer script contracts passed.
- Self-contained package build passed with exit code 0.
- Chrome and Edge extension package tests passed.
- Manifest contains 14 required files; 0 missing.
- `FocusGuard.Setup.exe`: 116,164,792 bytes; SHA-256:
  `F4B85D7FA2F8540B6D9893ED127523578C552709CFF9C2E259ACBE75D49B2D8D`
- Complete ZIP: 271,828,166 bytes; SHA-256:
  `67BD95E5F6DB745BB8D57C03AF13914349274608237546583B50CC82180BE427`
- Published Desktop DLL: 2,575,872 bytes; SHA-256:
  `C1E923563F5643654FC01BA09481D85B2D56BA32C87E90FF7603E0C2C8097932`
- Packaged Desktop binary contains the `Geist` resource marker.

## 5. UX implications

Installing from this package exposes the Geist typography, larger Bold profile
title, profile-colored timer gradient, and increased timer breathing room.

## 6. Security and recovery implications

The installer itself was not executed. No UAC prompt was automated and no
service, registry, ACL, browser policy, or current installation state changed.
The existing backup and rollback flow is packaged unchanged.

## 7. Limitations and next task

The generated setup still requires owner-present UAC approval to install or
update. Next step: run the documented owner-present installation and visual
verification if the user wants the current machine updated.
