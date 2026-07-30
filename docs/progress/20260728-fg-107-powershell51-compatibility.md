# FG-107 Windows PowerShell 5.1 compatibility checkpoint

## Summary

The first owner-present bundle build exposed that Windows PowerShell 5.1 does
not accept `utf8NoBOM` as a `Set-Content -Encoding` value. Manifest writing now
uses `System.IO.File.WriteAllText` with an explicit BOM-free UTF-8 encoder,
which works on Windows PowerShell 5.1 and PowerShell 7.

The attempted install did not reach installer preflight or mutation because
`Get-Service` was accidentally concatenated to the package argument after a
line-continuation backtick. No service or machine state was changed.

## Files changed

- `scripts/build-installer.ps1`
- `scripts/test-installer.ps1`
- this checkpoint

## Tests added

- Static validation rejects the unsupported `utf8NoBOM` encoding identifier and
  requires the compatible explicit UTF-8 encoder.

## Commands and results

- Windows PowerShell 5.1 parser and installer contract tests passed.
- Windows PowerShell 5.1 published service, recovery, and desktop successfully.
- Manifest and `FocusGuard-0.1.0-win-x64.zip` were created and parsed.
- The manifest records service `FocusGuard` and account
  `NT AUTHORITY\LocalService`.
- Read-only SCM inspection confirmed that the production service was not
  installed by either failed command.

## Security, UX, and recovery implications

No privileged operation was performed. The compatibility fix changes only
manifest serialization and does not change installer scope, ACLs, service
identity, recovery behavior, or rollback.

## Next task

Rerun the installer as a single command without a continuation backtick.
