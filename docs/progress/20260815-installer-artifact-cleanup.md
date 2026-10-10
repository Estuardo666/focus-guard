# Installer artifact cleanup checkpoint

## Summary

Audited `artifacts/installer` and retained the three documented 0.1.9 outputs:
the standalone setup executable, the complete ZIP archive, and the expanded
package used for packaged validation. Moved 26 superseded 0.1.0-0.1.8 files and
directories to the Windows Recycle Bin, reclaiming 11.06 GiB from the artifact
directory while preserving a recovery path.

## Files changed

- `TASKS.md`
- `docs/progress/20260815-installer-artifact-cleanup.md`
- Generated artifacts removed from `artifacts/installer` (Git-ignored)

## Tests added

No product tests were required; this checkpoint changes generated artifacts only.

## Commands executed

- Recursive installer artifact inventory and size audit
- Repository reference and Git-ignore audit
- Safe parent-path validation for every cleanup target
- Recycle-Bin cleanup of superseded artifacts
- 0.1.9 standalone package self-test, ZIP integrity check, and packaged installer validation

## Results

- Removed 26 obsolete top-level artifacts representing versions 0.1.0-0.1.8.
- Reclaimed 11.06 GiB from `artifacts/installer`.
- Retained `FocusGuard-0.1.9-Setup.exe`, `FocusGuard-0.1.9-win-x64.zip`, and
  `FocusGuard-0.1.9/`.
- The removed artifacts remain recoverable from the Windows Recycle Bin until it
  is emptied.

## UX implications

None. No shipped UI or installed component changed.

## Security implications

No executable was run with installation privileges and no service, registry,
policy, ACL, or installed application state changed.

## Recovery implications

The superseded generated artifacts can be restored from the Windows Recycle Bin.
They can also be regenerated from the corresponding source revisions if needed.

## Limitations

Historical binaries are no longer present directly in the artifact directory.
Historical documentation references remain as immutable checkpoint records.

## Next task

Complete the owner-present FG-159 single-click reinstall verification.
