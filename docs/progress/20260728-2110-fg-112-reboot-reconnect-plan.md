# FG-112 reboot reconnect plan

## Plan

1. Reproduce and record the post-reboot failure.
2. Allow authenticated local users to connect to the process-agent pipe while denying network access.
3. Keep the per-user agent alive across transient pipe authorization/startup failures.
4. Register both ProcessAgent and Desktop at user logon and cover installer contracts.
5. Build, test, reinstall, and verify a fresh dual acknowledgement.

Security: the service remains LocalService; the agent receives only bounded validated policy over a local pipe.
UX: Desktop opens at logon so an active/recovered session is visible.
Recovery: missing agent or extension releases both enforcement paths until fresh acknowledgements arrive.

## Checkpoint

1. Summary: the strict policy survived the real reboot and expired automatically, but enforcement remained released because ProcessAgent crashed with `UnauthorizedAccessException` connecting to the LocalService-owned pipe. Source now applies the same local-authenticated/network-denied ACL as the other service pipes, retries transient authorization failures, and registers Desktop at logon.
2. Files changed: `ProcessAgentPipeService.cs`, ProcessAgent `Program.cs`, installer contract, task log.
3. Tests added: installer startup registration assertions.
4. Commands executed: runtime service/process/registry/status diagnostics; direct agent launch; module builds; service and installer tests; installer build.
5. Results: service and agent builds pass with zero warnings; installer contract passes; one control-pipe integration test times out consistently after reboot; elevated deployment is pending because the current Codex process is not elevated.
6. UX implications: Desktop will open at logon after deployment.
7. Security implications: Network SID denied; authenticated local users receive only read/write access; service remains LocalService.
8. Limitations: source fix is not yet installed; a second real reboot is required.
9. Next task: owner runs the installer from an elevated PowerShell, then repeats a strict reboot drill.
