# FG-133 — Installer artifact refresh

## Summary

Regenerated the Windows installer bundle after the Desktop title-bar fix. The
existing `FocusGuard-0.1.0` artifact was held open by the installed Desktop
process, so the refreshed bundle was written to a new, explicit output folder.

## Results

- Package: `artifacts/installer/FocusGuard-0.1.0-updated/`
- Archive: `artifacts/installer/FocusGuard-0.1.0-win-x64-menu.zip`
- `FocusGuard.Setup.exe`, all production publishes, native manifests, browser
  extension archives, and `manifest.json` were generated.
- `scripts/test-installer.ps1` passed.
- The setup project initially exposed incompatible `GraphicsPath`/`DrawArc`
  overloads; these were corrected to the current System.Drawing API before the
  successful publish.

## Safety and limitations

The installer was built but not executed or elevated. The old default package
was not overwritten because another process held it open; the new package is
complete and independently archived.
