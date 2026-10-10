# FG-167 — Shared profiles, preferences and journal

Date: 2026-10-02. Status: **DONE** for this Phase 1 extraction milestone. Phase 1 overall and React/WebView2 remain pending.

## Summary

- Added `IProfileWorkflow`/`ProfileWorkflow` to Client. Reusable create/rename, localized bounded copy names, duplicate normal and break-rule IDs/cycle settings, rule add/remove and name conflict policies now live outside Avalonia. Existing profile/preset storage operations delegate to the unchanged Storage implementation.
- Moved DesktopSettings, AppTheme/SessionEndPresentation, resolver and store into `Client.Preferences`. Moved SessionIntention/SessionJournalStore into `Client.Journal`. JSON member names, numeric enum values, defaults, user-local paths and bounds are unchanged. DataDirectory/Persists became public C# accessors for shared host coordination; they are not frontend/IPC APIs.
- Added `SessionJournalWorkflow` for accepted preferences and intentions, observed-session journal fallback and reflection. Strict/cycle activation/repeat consent remains in existing UI/backend flows; no activation occurs in this workflow.
- Kept commands, event args and session-end option labels in DesktopPresentation.cs. Updated only required imports and Avalonia adapters. No AXAML/design changes, new runtime technology or backend operation.

## Execution location and source preservation

Worked in `D:/TEMP/focusguard-fg166-validation` because E: still reports Warning/Full Repair Needed. Compared 344 current source/build/assets files with the original E: checkout. Only Setup's build-generated icon differed; copied the original E: icon to D: before freezing the task baseline. Final build/tests/artifacts remain in D:; no disk repair or permissions change.

Baseline source hashes, affected file copies, pre-sync backups and receipts are under `artifacts/fg167/` in the D: copy. Before synchronization, verified each E: file still matched baseline and each new path was absent. Copied 19 validated files, then removed the two moved Desktop data files with explicit file edits. All 21 source changes were hash-verified against D:. Original pre-existing working-tree changes were preserved.

The first combined copy/delete/rollback command was rejected before execution by automatic review with only “blocked by policy”. A safer sequence of baseline checks/backups, copy-only commands and two explicit file edits completed synchronization. No rejected command was retried unchanged and no safety policy or ACL was bypassed.

## Files changed

- New Client: `Workflows/IProfileWorkflow.cs`, `Workflows/ProfileWorkflow.cs`, `Workflows/SessionJournalWorkflow.cs`, `Preferences/DesktopSettings.cs`, `Journal/SessionJournalStore.cs`.
- Desktop: `DesktopPresentation.cs`; extracted/removed DesktopSettings.cs and SessionJournalStore.cs; adapted FocusViewModel/FocusWorkflowViewModel/RuleEditorViewModel; imports only in App/MainWindow.
- Tests: `ProfileWorkflowTests.cs`, `UserDataCompatibilityTests.cs` added to Client.Tests; imports only in existing Desktop lifecycle, phase notification, viewmodel, journal and headless UI tests.
- Extension: manifest version metadata 0.1.16 → 0.1.17 only, required by existing package version checks; code/permissions/identity unchanged.
- Documentation: plan/checkpoint, architecture, migration progress and TASKS. Graphify generated outputs updated on original E: checkout.

No Domain/Application/Contracts/Service/Storage/Monitoring/Infrastructure.Windows/ProcessAgent/NativeMessaging/Recovery source changed. Core/session/clock/recovery/IPC/enforcement source hashes match the initial baseline.

## Tests and results

- Release build: zero warnings/errors.
- Full .NET: **429 passed, zero failed/skipped**, eleven suites: Application 4, Domain 34, Contracts 46, Infrastructure.Windows 15, Monitoring 12, NativeMessaging 23, Storage 18, Service 74, Desktop 158, Desktop.UiTests 14, Client 31.
- Added fourteen meaningful cases using real SQLite or literal legacy JSON: profile create/rename/delete; independent duplicate IDs/cycle rules/settings; unique localized copy names at 80 chars; invalid edits/cancellation without writes; add/remove rule semantics; built-in protection/custom presets; legacy preferences/journal; accepted cycles and unrelated settings; clearing cycle preference for normal timer; observed status not overwriting intent/preferences; optional corrupt journal/reflection; no UI/host dependency.
- Existing native Avalonia tests still cover strict repeat requiring fresh review, real storage, cycles/breaks and UI workflows.
- Initial new duplicate test fixture used an Allow break rule rejected by current real SQLite. Corrected fixture to the existing Block semantics and added explicit rejection verification; did not weaken Storage/Domain validation. Initial suite had one fixture failure; final suite passed unchanged production validation.
- Scoped `dotnet format ... --verify-no-changes --include <affected C# files>` passed.
- Task-scoped `git diff --check` passed. A broader Desktop diff check also surfaced pre-existing trailing whitespace in MainWindow.axaml; its baseline hash is unchanged and unrelated AXAML was not reformatted.
- `npm test` in D: passed 19/19. `npm run check` in E: passed using its existing dependencies; TS source hashes match the validated D: copy.
- `graphify update .` on E: passed: 4,662 nodes, 8,372 edges, 818 communities, AST-only.

Logs/receipts: `D:/TEMP/focusguard-fg166-validation/artifacts/fg167/{build-final.log,tests-final.log,test-results-final,format-verify.log,extension-tests.log,graphify.log,standalone-validation.log,source-sync-manifest.json,synced-source-receipt.json}`. Relevant commands: `dotnet build FocusGuard.slnx -c Release`, `dotnet test FocusGuard.slnx -c Release --no-build`, `npm test`, `npm run check`, `scripts/build-installer.ps1 -Version 0.1.17`, existing installer/drill/browser package validation scripts and standalone read-only modes.

## Installer validation

Created fresh standalone 0.1.17 in D:. Self-test and graphical smoke both exited 0. Embedded Desktop/Client SHA-256 match freshly published bytes. Installer script tests, FG-112 drill parsing and Chrome/Edge package tests passed. No installation or actual reboot/recovery drill was performed.

Standalone:

`D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.17-Setup.exe`

SHA-256: `6E8773B5C4427558244AE3E6A2720AF8800CA7D22FE8540818327FA08006F56F`

ZIP:

`D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.17-win-x64.zip`

SHA-256: `7CBBB7BFD57B7906E941046BE004F8C4FBD6E35F7B6976745070FA1E59A344E7`

Embedded Desktop DLL: `F69ECB62C16B9ADBFA73C7ECF2BCB92B8319A535FA77CAA27B517DBF7C86715F`

Embedded Client DLL: `3EC3CBC4CF21D93AAE49E4C0AFC6F224F6290BB54CBF5EB624C647DA6D78CBE9`

Validation-only child processes used RunAsInvoker to run package-file-hash/self-test/smoke under the existing non-admin token without UAC; environment setting restored afterward. Shipped Setup requireAdministrator manifest, accounts and ACLs unchanged. Extraction used D:/TEMP.

## UX, security and recovery implications

Profile edit locks, localized validation/confirmation, preset presentation and selected-profile reload remain in Avalonia. User-local data remains distinct from protected machine state. Accepted settings change only after accepted activation, journal failures retain existing optional/fallback behavior, and strict repeats still open fresh review. Profile edits/duplicates do not mutate active immutable service policies.

No new privileged command, bridge, pipe, HTTP listener, secret persistence or OS integration was introduced. This task does not resolve the already documented IPC authorization/agent lease/clock/deployment risks. Store hardening and move of storage abstractions remain later scoped work; raw inherited IProfileStore methods are a temporary C# boundary, not frontend permission to access files or bypass service rules.

## Limitations and next step

E: storage warning remains unresolved; final artifacts are deliberately in the healthy D: validation copy. No installed-process/browser/reboot/elevated recovery verification is claimed. Normal existing data limits/defaults were preserved, including 720-minute Desktop preferences/journal and the existing status-only observed-session fallback.

Next: finish shared preparation/activation coordination for cycles and strict review, preserving the backend handshake, two-minute preparation TTL, consent/recovery handoff and existing tests. Then address truthful stale/disconnected state and platform boundaries before introducing WebView2/React or declaring all of Phase 1 done.
