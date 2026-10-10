# FG-171 — Phase 3A: ProcessAgent listener availability

2026-10-03. Status: DONE_WITH_CONCERNS. This listener milestone is complete; Phase 3 as a whole is not.

## Debug report
Symptom: idle/partial peers held the sole endpoint indefinitely; zero/oversized/truncated frames interrupted the worker; cancellation could escape the loop.
Root cause: connected requests used only the host token, without a request lifetime; IOException-only catch excluded InvalidDataException and cancellation.
Fix: ProcessAgentPipeService.cs RequestTimeout=5s, injected TimeProvider deadline linked to host token, HandleConnectionAsync shares it through read/status/ack/audit/write. Expected malformed/disconnected/timed-out connections are isolated; shutdown cancellation returns normally. Idle accept remains tied only to host shutdown. Sequential maximum one instance and ACL/names/protocol remain unchanged.
Evidence: real GUID-named Windows pipes, unchanged ACL/framing. Before the fix (only optional pipe-name seam added), 11 regression cases yielded 8 failures and 3 passes; log artifacts/fg171/reproduction.log. Final solution passes all 481 tests, including 15 new listener cases. A real Generic Host continues running after an invalid frame.
Regression tests: tests/FocusGuard.Service.Tests/ProcessAgentPipeServiceTests.cs.
Related: migration plan baseline P0 availability row resolved; client-side confirmation/lease clearing and identity/roles remain open.

## Files changed
- src/FocusGuard.Service/ProcessAgentPipeService.cs: deadline/error isolation and isolated test pipe-name seam.
- tests/FocusGuard.Service.Tests/ProcessAgentPipeServiceTests.cs: 15 cases.
- extension/manifest.json: release metadata 0.1.21 only.
- TASKS.md, docs/architecture.md, docs/FOCUSGUARD_UI_MIGRATION_PLAN.md, this checkpoint and the task plan.

## Validation
- dotnet format FocusGuard.slnx --include the two owned C# files --no-restore: passed.
- dotnet build FocusGuard.slnx: 0 warnings/errors.
- dotnet test FocusGuard.slnx --no-build: 481 passed in 11 suites (Application4, Domain34, Contracts46, Infrastructure15, Monitoring12, Storage18, NativeMessaging23, Service89, Desktop171, Desktop.Ui15, Client54). All existing strict/persistence/recovery/channel/cycle tests retained.
- New coverage: zero/oversized/truncated header/truncated payload/invalid JSON/invalid request; idle/partial-header/partial-payload request recovery; connected and accepting shutdown; Generic Host survival; shared response-write deadline; exact policy acknowledgement and sanitized process audit with browser unconfirmed; first beat across an actual cycle boundary carries break policy and requires fresh ack.
- npm run check and npm test: 19 passed.
- scripts/build-installer.ps1 -Version 0.1.21: self-contained production installer regenerated. Resolved recursive cleanup paths verified inside D workspace; prior releases preserved.
- Standalone --package-self-test and --smoke-test passed; embedded Service.dll (all three distributed copies), ProcessAgent.dll and Desktop.dll match published DLLs. Service copies are identical.
- test-installer.ps1 and test-browser-extension-package.ps1 -Version 0.1.21 passed; test-fg112-reboot-drill.ps1 only parses scripts. No installed/reboot/recovery drill performed.
- Child-only RunAsInvoker used solely for readonly Setup test/hash commands, restored afterward. No installation/UAC/service/registry/ACL change.
- Protected source baseline verified: every backend source except the listener remains unchanged, including Agent Program, Contracts, Monitoring receiver, rules and recovery.
- graphify update . passed in D (2730 nodes / 4372 edges / 338 communities); original checkout refresh follows synchronization.

## Test harness corrections
Initial harness referenced a nonexistent in-memory audit type; replaced with the existing empty store, then a recording test-only store for audit assertions. An invalid test MatchKind was corrected to the real FileName contract. Immediate Start/Stop can cancel BackgroundService before ExecuteAsync is scheduled; the idle shutdown assertion now accepts successful or canceled completion, while rejecting a fault. Connected shutdown still requires successful completion. No production behavior was changed to accommodate fixtures.

## Artifacts
- --package-self-test: PASS
- --smoke-test: PASS
- publish\FocusGuard.Service\FocusGuard.Service.dll: 1E0EEA23136EAF71B96437F27536AE628545D62503C68010DDC7D9BFCF59152A
- publish\FocusGuard.ProcessAgent\FocusGuard.ProcessAgent.dll: 18D2D9BE67AEFB7266CE0D2CFD8DEF748249E23D8B24AD872540EE436AFB9CC9
- publish\FocusGuard.ProcessAgent\FocusGuard.Service.dll: 1E0EEA23136EAF71B96437F27536AE628545D62503C68010DDC7D9BFCF59152A
- publish\FocusGuard.Recovery\FocusGuard.Service.dll: 1E0EEA23136EAF71B96437F27536AE628545D62503C68010DDC7D9BFCF59152A
- publish\FocusGuard.Desktop\FocusGuard.Desktop.dll: B6E5B009B1DB49D0AFAFF88A4D4F7A2A1705D115DDA4DE0EC2EBBCE595138E94
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.21-Setup.exe: 32AD9D7A5D6315ACD33A9A7126F3CCA46563446E7648CC1897738958812EFAB6
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.21-win-x64.zip: 38FB2AA1257DEF77F53F3FD03A3E985CECEF7B465A3075A59838C6C6158C25A1

## UX/security/recovery implications
No screen or enforcement-rule change. A bad peer cannot hold one connected request forever or fault the worker through framing. The service remains authoritative and no retry/replay is introduced. A request deadline releases only that connection, never stops a session or changes persistence. Exact integrity/ack validation and channel-specific audit checks retained. Authorization is not strengthened here; broad authenticated-user ACL and repeated connection abuse remain Phase 4 work. Five seconds can temporarily delay the real agent; this is bounded availability, not complete denial-of-service protection. Independent browser channel semantics unchanged.

## Limitations and next milestone
Client Program still retains confirmation on pipe loss and lacks bounded request exchange; its hardcoded production endpoint also remains unchanged. Phase 3B must reproduce and correct client/receiver failure handling before a real new UI pilot. Phase 4 then adds caller/role/ACL identity gates. No installed enforcement, reboot, signed release or recovery parity claim.

## Synchronization
E drive still reports Warning / Full Repair Needed; builds and installers remain on healthy D NTFS. Only the eight task-owned source/docs paths are synchronized after checking E bytes against the captured baseline; backups and manifest in artifacts/fg171. Source icon generated by packaging excluded. No commits/staging or broad replacement of prior work. Final sync/graph receipts below.

Final receipts: all eight owned paths synchronized, source hashes verified and scoped git diff --check passed. Original graph updated to 4836 nodes / 8656 edges / 843 communities. Investigate root cause and shutdown scheduling learning recorded locally. Phase 3A complete; Phase 3B and Phase 4 gates remain open.
