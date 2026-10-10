# FG-172 — Phase 3B agent exchange, confirmation and lifecycle review

2026-10-03. DONE_WITH_CONCERNS for this milestone. The active goal includes all Phase 4 requirements; it remains active and is not redefined around Phase 3B.

## Summary / debug report
Original agent hardcoded the production endpoint, had no connected read/write deadline, retained confirmed policy on failed connection/EOF/invalid frames and did not check correlation or service readiness. Receiver had no local freshness deadline. The listener changed ServiceInstanceId on every reply. Stable-ID regression failed before the fix on actual isolated pipes.

Program now composes NamedPipeProcessAgentClient, ProcessAgentConnection, opt-in receiver lease and bounded AgentAuditStore with the unchanged controller/enforcement worker. Total exchange deadline 2s via TimeProvider; poll/reconnect interval 500ms. Agent confirmation expires after 3s monotonic elapsed time even during a hung actor, independently of wall-clock rollback. Failure/EOF/malformed/unrelated/not-ready/expired policy/shutdown resets local proof and held acknowledgement, never sends stop/recovery. Reconnection requires fresh policy then exact ID/hash acknowledgement. Service-instance change drops confirmation even for a restored same policy. Listener ID stable for its lifetime.

Endpoint and single-instance namespace follow compile flavor. Production mutex remains Local\FocusGuard.ProcessAgent.v1; development uses Local\FocusGuard.Development.ProcessAgent.v1. Startup discovery ignores agents in another Windows session. Agent audits bounded to 256, bound to producing session, consumed once before sending; uncertain sends may be lost but are not replayed or attributed to a new session.

## Files
ProcessAgent/Program.cs plus transport, connection, audit store, startup configuration and AssemblyInfo; Monitoring/ProcessMonitorPolicyReceiver.cs opt-in lifetime/reset; Service/ProcessAgentPipeService.cs stable ID; Platform.Windows/ProcessAgentStartup.cs same-session discovery; new ProcessAgent.Tests project (solution entry); Service.Tests stable ID; Desktop.Tests live supervisor child + TestTarget build dependency; test-installer.ps1 updated single-instance source check; extension release metadata only; TASKS, architecture, migration plan and task plan/checkpoint.

## Tests / commands / results
- Stable instance regression: FAILED before fix, actual GUID pipes; artifacts/fg172/reproduction.log.
- dotnet test ProcessAgent.Tests --property:FocusGuardProduction=true: 26 passed, production endpoint/mutex proof.
- dotnet format FocusGuard.slnx --include explicit owned C# files --no-restore: passed.
- dotnet build FocusGuard.slnx: 0 warnings/errors.
- dotnet test FocusGuard.slnx: 509 passed across 12 suites: Application4, Domain34, Contracts46, Infra15, Monitoring12, Storage18, NativeMessaging23, Service90, Desktop172, Desktop.Ui15, Client54, ProcessAgent26. artifacts/fg172/tests-final.log.
- Agent tests: failure reset/re-handshake, bad correlation/readiness/hash/state/confirmation, changed service ID, monotonic lease under rollback and pending reply, cancellation/shutdown, no audit replay/session mix, bounded queue, EOF/malformed/correlation/partial/silent/nonreading real peers, absent server/cancellation, real service reconnect with exact session ID/deadline and browser independent.
- Desktop lifecycle test launches cooperative TestTarget with finite 2s lifetime, observes actual exit/restart, cancels supervisor and verifies replacement stays alive until its natural exit. Launcher is injected to use TestTarget, not a real enforcement agent; tests never kill unrelated processes or contact installed pipes. Existing launcher duplicate/missing/retry/failure tests retained.
- npm run check and npm test: 19 passed.
- scripts/build-installer.ps1 -Version 0.1.22: production self-contained package regenerated after verified resolved scratch targets inside healthy D. Prior versions retained.
- Standalone --package-self-test / --smoke-test pass; six embedded hashes match published Service/Agent/Monitoring/Platform DLLs, including Service copies in Agent/Recovery. Child-only RunAsInvoker, readonly paths, restored afterwards.
- test-installer.ps1 and test-browser-extension-package.ps1 -Version 0.1.22 pass. test-fg112-reboot-drill.ps1 parses only.
- graphify update . in D: 2808 nodes / 4532 edges / 371 communities; original graph refreshed after sync.

## Harness corrections
Full concurrent suite exposed the test peer truncating request data with a 1-byte buffer and 150ms budget. Fixed normal-response test buffer to 4096 and finite deadline to 500ms (nonreading keeps 1-byte input); cancellation cleanup now contains expected truncated framing. Full rerun green. Installer static check expected old inline mutex literal; updated to assert Program uses build-flavor configuration, with real production/default tests retaining exact namespace coverage. Initial PowerShell async token probe was interrupted; rerun on Task.Run completed. No production behavior weakened for tests.

## Installer receipts
- publish_FocusGuard.Desktop_FocusGuard.Platform.Windows.dll.sha256: AF4A505BBD2D2F095A04461076316FBC2F4B20C723DC06EEEC8295A0F0FE0565
- publish_FocusGuard.ProcessAgent_FocusGuard.Monitoring.dll.sha256: 4171193450B8471AA69A5CCDB73DDEF407CFBFB5CFD178E6E56FF54838DED444
- publish_FocusGuard.ProcessAgent_FocusGuard.ProcessAgent.dll.sha256: F2E8294758B029D315EB11D0ADAAE0E899AD484A326930C5DD8B0050DDEEF458
- publish_FocusGuard.ProcessAgent_FocusGuard.Service.dll.sha256: 4543F8479728444BD387C24C9DC949A26A51235F4BF704D5971CAC2B56D02CB3
- publish_FocusGuard.Recovery_FocusGuard.Service.dll.sha256: 4543F8479728444BD387C24C9DC949A26A51235F4BF704D5971CAC2B56D02CB3
- publish_FocusGuard.Service_FocusGuard.Service.dll.sha256: 4543F8479728444BD387C24C9DC949A26A51235F4BF704D5971CAC2B56D02CB3
- artifacts/installer/FocusGuard-0.1.22-Setup.exe: 040EF7F60C01971DB1C646D31AFCC1636D4CEA85E6795648CC899CDFE72156D2
- artifacts/installer/FocusGuard-0.1.22-win-x64.zip: B03A9B5D48B7F2457525DAA12B76668255648D937BBB2ECE88529C115BA32511
- --package-self-test: PASS
- --smoke-test: PASS
- installer-script-tests: PASS
- extension-package-tests: PASS

## UX / security / recovery
No presentation change. Local failure releases only application confirmation; server session and browser channel remain intact. Finite policy expiration, strict persistence, recovery and protected process rules unchanged. Queue trades best-effort audit delivery for bounded memory and no uncertain replay. No installation, account/service/registry/ACL change. LocalService remains authoritative and no privileges widened. Native peer identity is not yet authenticated: Phase 4 remains required.

## Remaining scope / next task
Phase 3 component/lifecycle review completed; installed login/reboot/UI-close signoff remains explicitly separate. Supervisor still runs while Desktop is alive; closing Desktop cancels supervision, not the agent; login startup remains HKCU path. A new host must preserve this ownership.
Phase 4: provision actual owner independently of elevated installer identity; authenticated SID/logon/PID/image/hash roles; verify server against protected SCM identity; specific pipe rights excluding create-instance; remote/first-instance protections; all endpoint/client bounded faults; unauthorized and legitimate compatibility tests. Historical LocalService inability to inspect user processes is an architectural constraint. Native GUID pipe probe proves Impersonation token is already available BEFORE first message; simply switching Identification to Impersonation would expose tokens to a squatter. Keep Identification; investigate narrowly scoped readonly self-process metadata grant or another supported authenticated identity path. Probe result in artifacts/fg172/phase4-token-probe.txt.

## Synchronization
Owned paths copied only after comparing original E bytes against baseline, backups in artifacts/fg172, final hashes/diff check verified. E still reports Warning/Full Repair Needed; build artifacts remain D NTFS. Generated Setup icon excluded, prior modifications preserved. No commits/staging. Final receipts below.

Final sync: all 24 owned paths verified; scoped git diff --check passed. Original graph refresh completed; root cause and impersonation-before-message evidence retained for Phase 4. Overall goal remains active.
