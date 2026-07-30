# FocusGuard overnight progress

This log records autonomous, safety-bounded milestones. No privileged or real enforcement operations are performed.

## 2026-07-28 03:12 — FG-000 / FG-001

- Summary: Validated the repository structure and build scripts, then strengthened immutable domain validation.
- Files changed: `src/FocusGuard.Domain/Session.cs`, `src/FocusGuard.Domain/Rules.cs`, `tests/FocusGuard.Domain.Tests/SessionTests.cs`, `tests/FocusGuard.Domain.Tests/RuleTests.cs`.
- Acceptance criteria: repository specifications and ADRs read; restore/build/test baseline green; invalid session/rule inputs produce structured validation errors; rule values are normalized at construction helpers.
- Tests added: UTC start validation, strict recovery-delay validation, application-rule normalization, website query-parameter rejection.
- Commands: `C:\Program Files\dotnet\dotnet.exe restore .\FocusGuard.slnx`; `C:\Program Files\dotnet\dotnet.exe build .\FocusGuard.slnx --configuration Release`; `C:\Program Files\dotnet\dotnet.exe test .\FocusGuard.slnx --configuration Release`.
- Results: baseline was restore-successful, build-successful with 0 warnings, and 5/5 tests passed. Post-change validation is pending.
- Decisions: use explicit factory methods for validated rules while retaining records for immutable snapshots; require UTC input rather than silently changing caller intent.
- UX/security: no UI or system effect changed; stricter domain validation reduces malformed policy risk and keeps query parameters out of rule values.
- Limitations: no Git metadata exists in the supplied directory, so commits cannot be created; `dotnet` is not on this shell's PATH and is invoked by absolute path.
- Result: post-change restore passed, Release build passed with 0 warnings/errors, and 9/9 tests passed.
- Next task: FG-002 session transitions and expiration behavior.

## 2026-07-28 03:16 — FG-002

- Summary: Added idempotent state transitions and deterministic UTC expiration evaluation.
- Files: `src/FocusGuard.Domain/Session.cs`, `tests/FocusGuard.Domain.Tests/SessionTests.cs`, `TASKS.md`, `docs/progress/20260728-0316-fg-002.md`.
- Acceptance: same-state transitions are no-ops; active sessions expire at the exact end time; early and non-UTC checks do nothing.
- Result: restore passed, Release build passed with 0 warnings/errors, and 12/12 tests passed.

## 2026-07-28 03:18 — FG-003

- Summary: Added pure application path, folder, file-name, and supplied-hash matching.
- Files: `src/FocusGuard.Domain/ApplicationRuleMatcher.cs`, `tests/FocusGuard.Domain.Tests/ApplicationRuleMatcherTests.cs`, `TASKS.md`, `docs/progress/20260728-0318-fg-003.md`.
- Acceptance: normalized Windows paths match safely, folder sibling prefixes do not match, and no process APIs are used.
- Result: restore passed, Release build passed with 0 warnings/errors, and 15/15 tests passed.

## 2026-07-28 03:20 — FG-004

- Summary: Added URL normalization, wildcard matching, browser-internal bypass, and allow precedence.
- Files: `src/FocusGuard.Domain/WebsiteRuleMatcher.cs`, `tests/FocusGuard.Domain.Tests/WebsiteRuleMatcherTests.cs`, `TASKS.md`, `docs/progress/20260728-0320-fg-004.md`.
- Acceptance: bare domains, subdomains, path/scheme wildcards, query stripping, explicit exceptions, and default allow are covered.
- Result: restore passed, Release build passed with 0 warnings/errors, and 18/18 tests passed.
- Next task: FG-005 semantic Avalonia shell/accessibility milestone.

## 2026-07-28 03:22 — FG-005

- Summary: Refined the Avalonia shell with semantic color/control tokens, focus styling, active navigation state, and automation names.
- Files: `src/FocusGuard.Desktop/Styles/Tokens.axaml`, `src/FocusGuard.Desktop/Styles/Controls.axaml`, `src/FocusGuard.Desktop/MainWindow.axaml`, `TASKS.md`, `docs/progress/20260728-0322-fg-005.md`.
- Acceptance: shell controls use centralized semantic visual resources, have practical target sizing, expose automation names, and show visible focus/active state.
- Result: restore passed, Release build passed with 0 warnings/errors, and 18/18 tests passed. Static color audit passed for shell controls.
- UX/security: active location is communicated with surface plus rail; no enforcement or recovery behavior changed.
- Limitation: theme variants and live screen navigation remain queued.
- Next task: FG-006 inactive Focus screen with mock view model.

## 2026-07-28 03:26 — FG-006

- Summary: Connected the inactive Focus screen to a typed mock view model with duration presets, strict-mode explanation, and simulator-only start feedback.
- Files: `src/FocusGuard.Desktop/FocusViewModel.cs`, `src/FocusGuard.Desktop/MainWindow.axaml`, `src/FocusGuard.Desktop/MainWindow.axaml.cs`, `tests/FocusGuard.Desktop.Tests/`, `FocusGuard.slnx`, `TASKS.md`, `docs/progress/20260728-0326-fg-006.md`.
- Acceptance: data-bound profile/rule summary, duration selection, opt-in strict explanation, safe start feedback, typed compiled bindings, and explicit layout rows.
- Tests added: two desktop view-model tests covering safe defaults and strict-mode copy/action behavior.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, desktop tests 2/2 passed.
- UX/security: inactive simulator state is transparent; no actual restriction activation is possible from this screen.
- Limitation: active-session timing and real service connection remain queued.
- Next task: FG-007 accessible active-session Focus screen.

## 2026-07-28 03:32 — FG-007

- Summary: Added the active Focus view and an injectable-time mock session model with exact expiration and safe normal stop behavior.
- Files: `src/FocusGuard.Desktop/ActiveFocusViewModel.cs`, `src/FocusGuard.Desktop/ActiveFocusView.axaml`, `src/FocusGuard.Desktop/ActiveFocusView.axaml.cs`, `tests/FocusGuard.Desktop.Tests/ActiveFocusViewModelTests.cs`, `TASKS.md`, `docs/progress/20260728-0332-fg-007.md`.
- Acceptance: accessible remaining-time text, exact local end time, mode/health/rule counts, domain-backed expiration, and deterministic fake-clock tests.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, desktop tests 4/4 passed.
- UX/security: timer is dominant; stop and expiration are transparent simulator actions; no real enforcement is invoked.
- Limitation: service IPC and live navigation remain queued.
- Next task: FG-008 profile editor application-rule screen.

## 2026-07-28 03:40 — FG-008

- Summary: Added the application-rule editor mock screen with domain-backed validation, match-mode selection, empty state, and removable draft rows.
- Files: `src/FocusGuard.Desktop/ApplicationRuleEditorViewModel.cs`, `src/FocusGuard.Desktop/ApplicationRulesView.axaml`, `src/FocusGuard.Desktop/ApplicationRulesView.axaml.cs`, `tests/FocusGuard.Desktop.Tests/ApplicationRuleEditorViewModelTests.cs`, `TASKS.md`, `docs/progress/20260728-0340-fg-008.md`.
- Acceptance: block/allow actions, path/folder/file-name/hash modes, invalid-input feedback, normalized valid values, and accessible add/remove controls.
- Tests added: three editor tests for normalization, invalid input, and selective removal.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, desktop tests 7/7 passed.
- UX/security: draft-only behavior and future-session scope are explicit; no filesystem/process access or enforcement occurs.
- Limitation: profile drafts are not persisted or connected to service IPC.
- Next task: FG-009 profile editor website-rule screen.

## 2026-07-28 03:50 — FG-009

- Summary: Added the website-rule editor mock screen with domain-backed normalization, privacy guidance, empty state, and removable draft rows.
- Files: `src/FocusGuard.Desktop/WebsiteRuleEditorViewModel.cs`, `src/FocusGuard.Desktop/WebsiteRulesView.axaml`, `src/FocusGuard.Desktop/WebsiteRulesView.axaml.cs`, `tests/FocusGuard.Desktop.Tests/WebsiteRuleEditorViewModelTests.cs`, `TASKS.md`, `docs/progress/20260728-0350-fg-009.md`.
- Acceptance: block/allow actions, documented pattern examples, query-bearing input rejection, normalized drafts, validation feedback, and accessible add/remove controls.
- Tests added: three editor tests for normalization/privacy, invalid input, and selective removal.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, desktop tests 10/10 passed.
- UX/security: privacy boundary is explicit; no browser or browsing-history data is accessed.
- Limitation: profile drafts are not persisted or connected to service IPC.
- Next task: FG-010 strict confirmation review screen, UI only.

## 2026-07-28 04:00 — FG-010

- Summary: Added the strict-confirmation review screen with consequence scope, exact end time, browser health, recovery/unsaved-work gates, typed confirmation, and simulator-only status.
- Files: `src/FocusGuard.Desktop/StrictConfirmationViewModel.cs`, `src/FocusGuard.Desktop/StrictConfirmationView.axaml`, `src/FocusGuard.Desktop/StrictConfirmationView.axaml.cs`, `tests/FocusGuard.Desktop.Tests/StrictConfirmationViewModelTests.cs`, `TASKS.md`, `docs/progress/20260728-0400-fg-010.md`.
- Acceptance: dedicated review UI, finite end time, impacted apps/sites, browser health, recovery workflow copy, unsaved-work warning, exact sentence gate, explicit activation wording, and accessible control names.
- Tests added: three strict-review tests for safe defaults, all-gates behavior, and near-match rejection.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, desktop tests 13/13 passed.
- UX/security: consequence-first review is transparent; no code or strict session is generated or activated.
- Limitation: service acknowledgement and real recovery flow remain deferred.
- Next task: FG-011 versioned IPC contracts and validation.

## 2026-07-28 04:10 — FG-011

- Summary: Added standalone versioned IPC contracts with bounded JSON envelopes, typed status/activation messages, and validation helpers.
- Files: `src/FocusGuard.Contracts/FocusGuard.Contracts.csproj`, `src/FocusGuard.Contracts/IpcContracts.cs`, `tests/FocusGuard.Contracts.Tests/`, `FocusGuard.slnx`, `TASKS.md`, `docs/progress/20260728-0410-fg-011.md`.
- Acceptance: version/type/correlation validation, 64 KiB payload cap, depth limit, object-root requirement, typed activation round-trip, duration/recovery validation, and malformed-message rejection.
- Tests added: five contract tests covering valid envelopes, typed round-trip, malformed/oversized/versioned messages, activation constraints, and wrong message types.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, desktop tests 13/13 passed, contract tests 5/5 passed.
- Security: no transport, pipe, ACL, elevation, or CLR polymorphic deserialization was added.
- Limitation: transport/authentication/timeouts and service acknowledgement remain queued.
- Next task: FG-012 in-memory service simulator.

## 2026-07-28 04:20 — FG-012

- Summary: Added the in-memory authoritative service simulator with activation, status, expiration, and normal-stop behavior.
- Files: `src/FocusGuard.Service/FocusGuard.Service.csproj`, `src/FocusGuard.Service/InMemoryFocusService.cs`, `tests/FocusGuard.Service.Tests/`, `FocusGuard.slnx`, `TASKS.md`, `docs/progress/20260728-0420-fg-012.md`.
- Acceptance: ready/inactive status, validated activation, duplicate-session rejection, injected-time expiration, strict stop protection, and invalid-request state preservation.
- Tests added: five service tests covering initial state, activation, duplicate activation, expiration/re-activation, stop behavior, and invalid input.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, contract tests 5/5 passed, desktop tests 13/13 passed, service tests 5/5 passed.
- Security: memory-only simulator; no process, browser, pipe, persistence, elevation, or privileged operation.
- Limitation: transport, persistence, process monitoring, extension heartbeat, and desktop wiring remain queued.
- Next task: FG-013 SQLite schema and temporary-directory integration tests.

## 2026-07-28 04:30 — FG-013

- Summary: Added SQLite profile/rule schema and parameterized transactional store with temporary-directory integration tests.
- Files: `Directory.Packages.props`, `src/FocusGuard.Storage/FocusGuard.Storage.csproj`, `src/FocusGuard.Storage/SqliteProfileStore.cs`, `tests/FocusGuard.Storage.Tests/`, `FocusGuard.slnx`, `TASKS.md`, `docs/progress/20260728-0430-fg-013.md`.
- Acceptance: real SQLite round-trip, transactional rule replacement, cross-instance persistence, missing-profile behavior, cancellation, safe parameterization, and deterministic cleanup.
- Tests added: four storage integration tests using real temporary SQLite databases.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, contract tests 5/5 passed, desktop tests 13/13 passed, service tests 5/5 passed, storage tests 4/4 passed.
- Security: initial package choice was rejected by the vulnerability gate; the final dependency graph uses the patched native package and vulnerability audit reports no vulnerable packages.
- Limitation: only schema version 1 exists; secrets, policy integrity, and production data locations remain deferred.
- Next task: FG-014 process-monitor simulation with fake process snapshots.

## 2026-07-28 04:40 — FG-014

- Summary: Added a fake-snapshot process-monitor simulation with policy matching, allow precedence, protected exclusions, grace-period actions, and per-instance deduplication.
- Files: `src/FocusGuard.Monitoring/FocusGuard.Monitoring.csproj`, `src/FocusGuard.Monitoring/SimulatedProcessMonitor.cs`, `tests/FocusGuard.Monitoring.Tests/`, `FocusGuard.slnx`, `TASKS.md`, `docs/progress/20260728-0440-fg-014.md`.
- Acceptance: graceful-close then termination proposal, injected time, expired-policy fail-open, allow-over-block precedence, protected snapshot exclusion, and sanitized/deduplicated actions.
- Tests added: five monitoring tests covering grace timing, precedence, protected processes, deduplication, and expiration.
- Result: restore passed, Release build passed with 0 warnings/errors, domain tests 18/18 passed, contract tests 5/5 passed, desktop tests 13/13 passed, service tests 5/5 passed, storage tests 4/4 passed, monitoring tests 5/5 passed.
- Security: no real processes or OS APIs were accessed or manipulated.
- Limitation: real process source and action sink remain deferred by the safety boundary.
- Next task: FG-015 TypeScript browser rule engine and tests.

## 2026-07-28 04:50 — FG-015

- Summary: Added dependency-free TypeScript browser URL rule engine with normalization, wildcard matching, precedence, browser-internal fail-open behavior, and privacy sanitization.
- Files: `extension/package.json`, `extension/tsconfig.json`, `extension/src/rule-engine.ts`, `extension/test/rule-engine.test.ts`, `extension/README.md`, `TASKS.md`, `docs/progress/20260728-0450-fg-015.md`.
- Acceptance: documented URL patterns, query/fragment removal, allow-over-block precedence, browser-internal bypass, malformed URL fail-open, and reusable compiled rules.
- Tests added: six Node tests.
- Result: `npm test` passed 6/6 with no warnings; .NET restore/build/test passed with 50/50 tests and 0 build warnings/errors.
- Security: no browsing history transmission, extension installation, browser policy, or native messaging was performed.
- Limitation: Manifest V3/native messaging packaging and heartbeat remain queued or owner-present.
- Next task: FG-016 CI, formatting, coverage, and overnight report automation.

## 2026-07-28 05:00 — FG-016

- Summary: Added repeatable local/CI validation for restore, formatting, Release build, .NET tests, TypeScript tests, coverage collection, and overnight-report structure checks.
- Files: `scripts/ci.ps1`, `scripts/format.ps1`, `scripts/test.ps1`, `scripts/verify-report.ps1`, `.github/workflows/ci.yml`, `.gitignore`, `TASKS.md`, `docs/progress/20260728-0500-fg-016.md`, and `OVERNIGHT_REPORT.md`.
- Acceptance: formatting verification, zero-warning Release build, all automated tests, TypeScript tests, coverage artifacts, and required overnight-report headings are checked by one CI entry point.
- Tests added: no product behavior tests; validation automation now executes the existing 50 .NET and 6 TypeScript tests and collects Cobertura artifacts.
- Result: `pwsh .\scripts\ci.ps1` passed; formatting passed; Release build passed with 0 warnings/errors; .NET tests passed 50/50; TypeScript tests passed 6/6; report validation passed.
- UX/security/recovery: automation only validates safe simulators and documentation; no process, service, browser, persistence, elevation, or recovery behavior was activated.
- Limitation: GitHub Actions has not been executed remotely from this non-Git workspace; owner-present integration milestones remain gated.
- Next task: owner-present review of FG-100 and later integration milestones.
