# FG-173 — bounded IPC owner and payload policy provisioning

2026-10-03. Helper and fixture-only checks are complete. This checkpoint provisions a concrete policy record for later Phase 4 integration; it does not authenticate a peer or enable the policy in the installed service.

## Plan and implementation

Read `AGENTS.md`, the migration plan's “Autenticación y autorización local” section, and the FG-172 Phase 4 follow-up. The security design requires an explicitly provisioned interactive owner SID, role-bound payload identity, no trust in client-supplied identity, and narrow IPC authorization. This slice adds only a standalone policy generator and isolated PowerShell fixture test; installer, Setup, service, ACL, registry, and live endpoint integration remain with the owning milestone.

`scripts/New-FocusGuardIpcPolicy.ps1` accepts an explicit `OwnerSid`, an existing `InstallRoot`, and an `OutputPath` that must resolve to `InstallRoot\ipc-policy.json`. It never queries or defaults to the PowerShell process identity. The accepted forms are a conventional `S-1-5-21-...` user SID or `S-1-12-1-...` Azure AD user SID; SYSTEM, LocalService, group SIDs, malformed values, and overflowing SID components are rejected.

The emitted version 1 JSON has this shape:

```json
{
  "schemaVersion": 1,
  "ownerUserSid": "S-1-5-21-...",
  "installRoot": "C:\\Program Files\\FocusGuard",
  "service": {
    "serviceName": "FocusGuard",
    "fileName": "FocusGuard.Service.exe",
    "sha256": "<64 uppercase hex characters>",
    "managedFileName": "FocusGuard.Service.dll",
    "managedSha256": "<64 uppercase hex characters>"
  },
  "binaries": [
    {
      "role": "Desktop",
      "fileName": "FocusGuard.Desktop.exe",
      "sha256": "<64 uppercase hex characters>",
      "managedFileName": "FocusGuard.Desktop.dll",
      "managedSha256": "<64 uppercase hex characters>"
    }
  ]
}
```

The `binaries` array always contains `Desktop`, `ProcessAgent`, and `NativeMessaging` records with their matching managed assemblies. `WebViewHost` is included only when explicitly supplied and is fixed to `FocusGuard.UI.exe` plus `FocusGuard.UI.dll`. `Diagnostic` requires an explicit executable and managed assembly descriptor. Aliased names across roles are rejected. Only the listed executable and corresponding managed assembly leaves are hashed; this does not create a directory-wide allowlist.

The helper rejects a missing installation root or payload file, reparse-point path traversal, reparse-point payload/output files, subdirectory or traversal filenames, and output paths outside the installation root. SHA-256 reads open each payload with read sharing only. Normal invocation writes UTF-8 JSON through a same-directory temporary file and atomic move/replacement; `-DryRun` returns the same manifest without writing, and `-WhatIf` suppresses writing through `ShouldProcess`.

The helper does not set ACLs or prove the root's ACL. A real installer caller must first protect the installation root and generated `ipc-policy.json` with its existing installer ACL, then verify those protections before any service code treats the record as trusted. Atomic file creation does not authenticate the interactive user or the binaries, and it does not establish logon/session scope. The later loader must compare the service identity to protected SCM state and bind role/client identity to a validated native peer; owner logon/session scope must come from that authenticated peer, never a payload field. Client singleton scope remains per service lifetime to prevent role or session reuse. No pipe client, role check, endpoint change, registration, or enforcement hook is added here.

## Tests and commands

`scripts/test-ipc-policy-provisioning.ps1` creates a GUID-named direct child of the D: workspace containing fixture directories with fake bytes, invokes only the policy parser/provisioner, and removes the fixtures. Before cleanup it canonicalizes the workspace, test root, and junction paths; it verifies the test root is the expected named direct child, confirms the alias is a reparse point directly under that root, removes the junction without recursion, and only then recursively removes that verified test root. `Remove-Item` on the `mklink /J` target throws a PowerShell 7 provider `NullReferenceException` on this host, so the test uses `[IO.Directory]::Delete(path, $false)` for the non-recursive junction unlink. It checks explicit owner handling (including Azure AD SID form), SHA-256 for every executable and managed assembly, required payload failures, SYSTEM/LocalService/group/malformed owner rejection, filename traversal and duplicate-role aliases, reparse traversal through a temporary directory junction, dry-run non-writing behavior, JSON round-trip, and atomic replacement.

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\test-ipc-policy-provisioning.ps1` — passed.

No production assembly or installer changed, so no product build, package generation, service operation, ACL edit, registry edit, process launch, elevation, or live identity test was run. Fixture hashes and junction behavior do not prove installed identity or protected ACLs.

## Security and recovery implications

The owner value is explicit input and has a bounded SID syntax; the helper does not assume that the installer process is the owner. Payload identity is tied to one fixed service executable/assembly pair and the three required client roles. Each optional role is opt-in and has fixed direct-child file names or an explicit diagnostic descriptor. Policy creation only writes inside the caller-provided installation root and does not broaden a Windows ACL.

This is a provisioning primitive, not the Phase 4 gate. Before registration or use, the integrating installer must ensure the current installer ACL protects both payload and policy. The service must fail closed on invalid schema, owner or path mismatch, hash mismatch, missing files, malformed records, role mismatch, or untrusted policy ACL. Caller SID/logon/PID/image verification, remote and first-instance pipe protections, least-rights pipe ACLs, compatibility tests, and recovery/rollback verification remain outstanding. No recovery state or running service is touched by this milestone.

## Files and next step

- Added `scripts/New-FocusGuardIpcPolicy.ps1`.
- Added `scripts/test-ipc-policy-provisioning.ps1`.
- Added this progress note.

Next: root integrates generation into installer rollback after the existing installation ACL is in force, then adds a strict service-side loader and authenticated peer/role checks. The installer and Setup source files were intentionally left to their owner.
