# FG-166 — Shared client extraction and normal timer workflow

Date: 2026-10-02. Implementation: **DONE** for Phase 1A and the status/normal-timer substep of Phase 1B. Environment diagnostic: **DONE_WITH_CONCERNS**; E: storage condition remains unresolved.

## Summary

Created `FocusGuard.Client` and moved the existing `IFocusControlClient`, `NamedPipeFocusControlClient` and workflow partial out of the Avalonia assembly. Existing transport bodies match the pre-task baseline, allowing namespace/formatting changes only. Codecs, framing, protocol v2, five-second timeout, correlation checks and Identification impersonation level are unchanged.

`ISessionWorkflow` / `SessionWorkflow` pass through service status and start normal timers by loading the actual saved profile and copying its rule snapshot into the existing activation DTO. Normal requests cannot include strict mode, recovery credentials or recovery delays; enforcement remains an explicit opt-in. Duration limits remain the backend's one-minute to 30-day bounds; current Desktop inputs/settings retain their existing 720-minute bound.

Avalonia uses this workflow for status/normal starts and keeps its existing strict/cycle paths, review, settings, journal, presentation and lifecycle. A profile deleted after refresh is rejected before sending activation, with localized EN/ES feedback. A SQLite read error during activation is displayed safely. No automatic activation retry was added. Backend state remains authoritative.

DrillClient references Client rather than Desktop. ProcessAgent is now included in the solution build. No React or WebView2 project was created.

## Files changed

- Added `src/FocusGuard.Client/FocusGuard.Client.csproj`, extracted interface/transport/partial, `Workflows/ISessionWorkflow.cs`, `Workflows/SessionWorkflow.cs`.
- Removed the old Desktop transport files after copying/extracting them into Client.
- Desktop: added Client reference, adapted FocusViewModel; updated imports in BlockScreenViewModel, SessionSummaryViewModel, ServiceHealthCheck and Program; added one resource key in each EN/ES resource file.
- DrillClient: project reference and import only.
- Solution: added Client, Client.Tests and existing ProcessAgent.
- Tests: added Client.Tests project/GlobalUsings/SessionWorkflowTests; moved the two FocusControlClientTests from Desktop.Tests; added two Desktop normal-start regressions; updated dependent test imports/references.
- Documentation: task plan, this checkpoint, TASKS, architecture and migration-plan progress.
- Extension: **version metadata only**, 0.1.15 → 0.1.16, required by the existing coherent installer/browser package-version checks. Extension logic, permissions and identity unchanged.
- Updated graphify generated outputs after code changes.

The pre-existing modified/untracked working tree was preserved. Baseline copies, source hashes and initial Git status are under `E:/FocusGuard/FocusGuard-Autopilot/artifacts/fg166/baseline/`. Protected Domain/Application/Contracts/Service/Storage/Monitoring/Infrastructure.Windows/ProcessAgent/NativeMessaging/Recovery source hashes match that baseline, including in the final validation copy.

## Tests added and preserved

- Client transport tests relocated without losing coverage: wrong correlation and missing-pipe timeout.
- Fifteen workflow cases: SQLite snapshot/Block-Allow rules and explicit opt-in; current saved profile and immutable accepted snapshot; missing profile; invalid/valid duration limits; cancellation; activation timeout without replay; strict/cycle/degraded status passthrough; transport failure propagation; real v2 pipe activation/status/new-client rehydration and active-session rejection; no Desktop/Avalonia/Service/Win32 dependency.
- Two Desktop regressions: deleted selected profile and corrupt SQLite on start do not activate or crash and leave the action available.
- Existing strict/cycle/recovery/clock/channel/storage/native Avalonia tests retained. No live OS process blocking or installed-service test was enabled.

## Commands and results

- Scoped `dotnet format ... --verify-no-changes --no-restore --include <explicit affected C# files>` passed after import/EOL normalization. No broad backend reformatting.
- `dotnet build FocusGuard.slnx --configuration Release --nologo`: passed, zero warnings/errors, on original E: source and the isolated D: validation copy.
- `dotnet test FocusGuard.slnx --configuration Release --no-build --nologo`: **415 passed, zero failed/skipped**, eleven suites. Application 4; Contracts 46; Domain 34; Infrastructure.Windows 15; Monitoring 12; NativeMessaging 23; Storage 18; Service 74; Desktop 158; Desktop.UiTests 14; Client 17.
- `npm run check` and isolated `npm test` in the original extension: passed, **19/19**. Code bytes were unchanged and verified in the validation copy.
- `graphify update .`: passed, final report 4,581 nodes, 8,260 edges, 824 communities; AST-only, no API usage.
- `scripts/build-installer.ps1 -Version 0.1.16`: final build passed in the validated D: copy.
- Standalone `--package-self-test`, `--smoke-test`: both exit 0.
- Standalone `--package-file-hash` for **Desktop.dll and Client.dll** matched their freshly published bytes.
- Installer script tests, FG-112 drill script parsing, Chrome/Edge 0.1.16 package tests: passed. Parsing a reboot script is not performing a reboot drill.

Initial verification exposed an assertion against private `IsBusy` and two new tests incorrectly assuming SQLite rule order. Tests were corrected to use observable command state and match rules by stable IDs. Initial formatting verification found imports/EOL issues; explicit file-scoped formatting resolved them. No production engine behavior was changed to make tests pass.

Original E: logs: `artifacts/fg166/{build-final.log,tests-final.log,extension-check.log,extension-tests.log,graphify.log}`. Final artifact/build/test/format/package receipts: `D:/TEMP/focusguard-fg166-validation/artifacts/fg166/`. The final TRX log/summary selects one final run rather than summing repeated audit runs.

## Validated installer artifacts

Final standalone:

`D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.16-Setup.exe`

SHA-256: `86E2ECFB511A82FF77725212A3567D5B652F211FBFA795EE72F42F63F8D72B60`

Complete ZIP:

`D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.16-win-x64.zip`

SHA-256: `A44EB1371EE5B1C296D55B2F615D32042DD88BB7C80586D9956D34D99D700331`

Embedded/published Desktop DLL: `49C964B1186A778BE5E9A3203775A4A9EDB294E807A85B1874171B39503F5C96`

Embedded/published Client DLL: `3E181FADC6786F62D776F73E55C6C83A8425B6522DD8B2EF4E680C45EE58607A`

The earlier E: candidate was superseded by this final validated artifact; do not confuse its hashes with the D: output. Self-contained publication depends on build/source paths, so hashes of separate builds need not match despite identical source bytes.

## Environment investigation

The first E: candidate built and passed self-test/smoke, but the final rebuild failed recreating `artifacts/installer/FocusGuard-0.1.16/publish` with access denied. Read-only diagnosis reproduced the inaccessible directory. Windows reports E: as exFAT, `HealthStatus=Warning`, `OperationalStatus=Full Repair Needed`; D: is NTFS, Healthy/OK. No Setup process remained running. This is evidence of an output/storage problem, not proof of a specific hardware failure or application-code defect.

Used the investigate workflow to separate source changes from environmental failure. No filesystem repair, ACL/account change, service operation or privilege elevation was attempted. The diagnostic spans the external volume rather than one source module, so no source-edit freeze was applied; no diagnostic source fix was made.

Copied the current working tree into a new isolated `D:/TEMP/focusguard-fg166-validation` directory excluding .git/bin/obj/artifacts/node_modules/graphify output. Verified 298 source/test/script/extension files plus solution/build metadata and assets against E: with SHA-256. A fresh build, full tests and packaging all succeeded there. This mitigates validation risk; it does not repair E:.

All installer validation used only `--package-file-hash`, `--package-self-test` and `--smoke-test`. The calling token was non-elevated; child-only `__COMPAT_LAYER=RunAsInvoker` allowed these read-only modes without UAC and was restored afterward. The shipped Setup `requireAdministrator` manifest was unchanged. Temporary extraction was directed to D:/TEMP; no installation mode was executed.

## UX, security and recovery implications

- HIGH RISK extraction is contained to client-side normal activation and backed by existing and new tests. Normal UI appearance/settings and opt-in remain unchanged.
- Client never implements enforcement, expiration, cycle progression, recovery, Win32 termination or elevated operations. No new IPC message type, ACL, auth bypass or HTTP listener was introduced.
- Status exceptions propagate from shared workflow without invented idle state. Existing Avalonia status-error presentation remains unchanged; the planned stale/disconnected state is still a future migration substep.
- Strict review/credentials/persistence, immutable signed snapshots, normal/strict stop rules, recovery and independently confirmed channels stay in the backend paths already used by the product.
- Installer is created and validated, **not installed**. Existing source-level P0 pipe authorization/agent deadline/lease risks and installed/reboot/native signoffs remain pending as documented in the plan.

## Next scoped step

Continue the remaining Phase 1 extraction of profile CRUD/preset/rule workflows and user preferences/journal, with thin Avalonia adapters and preserved data formats. Keep React/WebView2 and IPC/agent security fixes in their separate planned milestones. Before more large writes to the original workspace, resolve the owner-managed E: storage condition or select a healthy primary checkout; this task has not repaired or relocated the original repository.
