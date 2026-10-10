# FG-168 — Shared cycle and strict activation coordination

Date: 2026-10-02. Implementation: **DONE in the D: validated source copy**. Synchronization to original E: checkout: **PENDING — drive absent**. Phase 1 overall and WebView2/React remain pending.

Follow-up FG-169: E: returned; this milestone's pending source/doc changes were included in the cumulative guarded synchronization alongside FG-169. See its final checkpoint/receipt for completion; the original pending statement above records the state at FG-168 delivery.

## Summary and changes

- Extended ISessionWorkflow with reviewed CycleSessionInput/StrictTimerInput, normal-cycle start result, prepare/activate, strict timer, stop and skip operations.
- Added SessionActivationWorkflow.cs as a partial of SessionWorkflow. Existing normal/status behavior is unchanged. Snapshot rule/configuration mapping now lives in Client; no profile/rule reload occurs after review.
- Normal cycles perform one prepare then one activation with its exact returned preparation ID and no recovery credential. Rejected preparation does not activate. The one-step path rejects strict inputs, requiring separate review/activation.
- Strict timer requests preserve strict=true, finite duration, explicit enforcement, the reviewed rule snapshot and the existing hashed credential/recovery delay. Plaintext recovery codes are not transmitted or stored by Client.
- Prepared activation does not retry, reconstruct a plan or prepare again if a response is lost. Service retains consume-once, TTL and strict-state authority. Stop/skip forward to service rules.
- Avalonia uses shared workflow methods. Existing review/CanConfirmStrict/two-step consent, pending preparation display, code generation/display/cancel/clearing, accepted settings, repeat, notifications and error presentation remain in Desktop.
- Added SessionActivationWorkflowTests.cs; no service/domain/contract/consumer changes. Extension manifest version only: 0.1.17 → 0.1.18 for coherent package-version validation.

Files: Client/Workflows/{ISessionWorkflow.cs,SessionWorkflow.cs,SessionActivationWorkflow.cs}; Desktop/FocusViewModel.cs; Client.Tests/SessionActivationWorkflowTests.cs; extension/manifest.json; task/architecture/migration/checkpoint docs and generated graph. The first four existing source files and two new files are listed with pre-task hashes in `artifacts/fg168/source-sync-manifest.json`. No deletes are required.

## Baseline and storage

The existing D:/TEMP/focusguard-fg166-validation copy contains the previously synchronized FG-167 source. E: was absent at task start and remained absent during final checks; no repair, account/ACL change or replacement of the original checkout was attempted. All work/artifacts/checkpoints are in D:, which reports Healthy/OK. Do not claim E: contains FG-168 yet.

Source hashes and affected-file copies: `artifacts/fg168/baseline/`; task-local diffs: `artifacts/fg168/*.diff`. Protected Domain/Application/Contracts/Service/Storage/Monitoring/Infrastructure.Windows/ProcessAgent/NativeMessaging/Recovery hashes match baseline. The storage warning/absence does not authorize disk repair or overwriting later owner edits. If E: returns, compare manifest OldHash before copying each file; also synchronize task/documentation progress explicitly. Never copy bin/obj or replace unrelated work.

## Tests and validation

- Release solution build: zero warnings/errors.
- Scoped full formatting verification: passed for five affected C# files.
- Full .NET: **442 passed, zero failed/skipped** across eleven suites. Application 4; Domain 34; Contracts 46; Infrastructure.Windows 15; Monitoring 12; NativeMessaging 23; Storage 18; Service 74; Desktop 158; Desktop.UiTests 14; Client 44.
- Thirteen new cases: normal/strict preparation snapshot/rules/break settings; no activation during preparation; one preparation/exact-ID normal start; rejected preparation/no activation; strict cannot use one-step start; strict timer reviewed snapshot and hash-only credential; invalid/missing credential rejection; lost activation response/no retries; cancellation without storage/send; real v2 pipe TTL, normal skip and stop; real strict prepared snapshot despite later profile edit, consume-once and denied skip/stop; characterization of current Block-storage/Allow-prepare mismatch.
- Existing native Avalonia tests still cover strict repeat requiring fresh review, two-step review/recovery, cycles and UI state. No mocks replace the product's real client. Recording clients and RAM strict persistence are isolated test fixtures; installed persistence/reboot recovery is not claimed.
- Initial new fixtures had Block break rules rejected by the unchanged preparation codec; corrected fixtures to codec-valid Allow and added an explicit mismatch characterization case. A real-pipe test incorrectly used reference identity for a deserialized credential; corrected to record value equality. No backend/contract validation was weakened to pass tests.
- `npm ci --ignore-scripts` installed only locked local dev dependencies in D:/extension because E: dependencies were unavailable. Lockfile/package.json unchanged. TypeScript check passed and isolated extension tests passed **19/19**.
- `graphify update .` in the D: copy: passed, 2,524 nodes, 3,903 edges, 332 communities. This graph describes the isolated copy's corpus and is not comparable numerically with the original E: graph; AST-only, no API calls.

Commands: `dotnet build FocusGuard.slnx -c Release --no-restore`; `dotnet test FocusGuard.slnx -c Release --no-build`; explicit-file `dotnet format ... --verify-no-changes`; `npm ci --ignore-scripts`; `npm run check`; `npm test`; `scripts/build-installer.ps1 -Version 0.1.18`; installer/drill parse/browser package validations; standalone read-only modes. Logs/TRX/format/source hash/package receipts: `D:/TEMP/focusguard-fg166-validation/artifacts/fg168/`.

## Existing break-rule mismatch — separate HIGH RISK debt

Source evidence: Storage/SqliteProfileStore.ValidateProfileCycleSettings permits Block-only break rules; Contracts/CycleIpcProtocol.ValidatePrepareRequest calls ValidateRules(..., allowOnly:true), which permits Allow-only break rules. A real SQLite Block setting therefore fails preparation validation. This existed before extraction; the NamedPipe client previously used the same codec.

FG-168 preserves both implementations and the failure rather than silently converting rule actions or modifying enforcement. Existing default cycles with empty break rules still work. Non-empty break-rule parity must be resolved as a dedicated, reviewed task with explicit intended semantics and regression tests across Storage, preparation, policy integrity, ProcessAgent and browser consumers before claiming that case complete.

## Installer artifacts

Standalone:

`D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.18-Setup.exe`

SHA-256: `31CE8D385EB7A7E60086598C1A56FE1A6769B3CAED4A05E1E1E9D6863A05DE80`

ZIP:

`D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.18-win-x64.zip`

SHA-256: `0B6D84CE21344D9F1E3ADF219802C63C0BADEF61C0ABD31E492F26214BDFD02A`

Embedded Desktop.dll: `46BA6B4FE32727C860737754434468DD6CB66CAAC521CF4A9E47F2388BE2EA72`

Embedded Client.dll: `0E5EB19944238E5D12A95C7FE068809EFA6D1ABA07EF4B8DE463306369377C52`

Standalone self-test and graphical smoke both exit 0; embedded DLL hashes match fresh publish. Installer script tests, FG-112 drill parsing and Chrome/Edge package validations passed. No installation/reboot/real-process enforcement was performed. Child-only RunAsInvoker was restricted to read-only package validation modes under the existing non-admin token; restored afterward. Shipped administrator manifest/accounts/ACLs unchanged. Extraction uses D:/TEMP.

## UX, security and recovery implications

HIGH RISK extraction preserves reviewed snapshots, explicit opt-in, hash credential and prepare/activate IDs. No JS bridge/HTTP server/privileged API was added. Client does not become a time, phase, policy, persistence or consent authority. Strict review remains mandatory in Avalonia, one-step shared cycle start excludes strict, and service still refuses normal stop/skip for strict. No logs/persistent frontend data contain recovery plaintext.

Prepared expiration/rejection is returned as backend rejection, not reinterpreted as success or retried automatically. The backend/recovery implementation and its original tests remain intact. These tests validate client/contract wiring and in-process behavior, not installed anti-tamper, reboot or owner-present recovery.

## Next step

Address stale/disconnected UI state without fabricating idle or changing authoritative expiration; preserve the last backend snapshot and disable state-changing actions until reconciled. Keep IPC authorization/agent lease fixes and the break-rule semantic mismatch in their dedicated planned tasks. Restore/synchronize the original checkout only when the drive returns and baseline checks pass; do not consider the entire Phase 1 or functional parity complete yet.
