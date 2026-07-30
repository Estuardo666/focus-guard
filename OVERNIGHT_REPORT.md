# FocusGuard overnight report

## 1. Executive summary

Completed the first sixteen safe, non-privileged milestones: repository/build validation, immutable domain validation, session transitions/expiration, application/website rule matching, semantic Avalonia shell styling, inactive Focus mock screen, active Focus timer screen, application-rule editor, website-rule editor, strict-confirmation review UI, versioned IPC contracts, in-memory service simulator, SQLite profile/rule persistence tests, fake process-monitor simulation, TypeScript browser rule engine tests, and repeatable CI/formatting/coverage/report validation. No Windows services, process actions, browser policies, elevated operations, or production persistence paths were used.

## 2. Final validation

- Restore: passed.
- Release build: passed with 0 warnings and 0 errors.
- Release tests: 50 passed, 0 failed, 0 skipped (18 domain, 13 desktop, 5 contracts, 5 service, 4 storage, 5 monitoring).
- TypeScript tests: 6 passed, 0 failed, via `npm test`.
- UI static audit: passed for semantic shell color usage and automation names.
- Desktop view-model tests: 13 passed, 0 failed.
- Contract tests: 5 passed, 0 failed.
- Service tests: 5 passed, 0 failed.
- Storage tests: 4 passed, 0 failed.
- Monitoring tests: 5 passed, 0 failed.
- Vulnerability audit: no vulnerable packages reported for any solution project.
- Formatting verification: passed with `dotnet format --verify-no-changes`.
- Full validation script: passed via `pwsh .\scripts\ci.ps1`.
- Coverage: Cobertura artifacts generated under `artifacts/test-results/`.
- Overnight report structure validation: passed.
- SDK invocation: `C:\Program Files\dotnet\dotnet.exe` because `dotnet` is not available on the current shell PATH.

## 3. Completed milestones

- FG-000: validated repository structure and scripts.
- FG-001: added structured validation for session and rule construction helpers.
- FG-002: added idempotent transitions and UTC expiration evaluation.
- FG-003: added pure application path/folder/file-name/hash matching.
- FG-004: added URL normalization, wildcard matching, browser-internal bypass, and allow precedence.
- FG-005: added semantic shell tokens, focus styling, active navigation state, and automation names.
- FG-006: added inactive Focus bindings, duration presets, strict-mode explanation, and simulator-only action feedback.
- FG-007: added active Focus timer, exact end time, health/status display, and fake-clock expiration tests.
- FG-008: added application-rule editor with domain validation, draft rows, empty state, and remove actions.
- FG-009: added website-rule editor with pattern normalization, privacy guidance, validation, and remove actions.
- FG-010: added strict-confirmation review UI with consequence scope, recovery/unsaved-work gates, exact sentence, and safe simulator status.
- FG-011: added versioned bounded IPC envelopes and typed activation/status contracts with validation tests.
- FG-012: added memory-only authoritative service simulation for activation, expiration, status, and normal stop behavior.
- FG-013: added parameterized transactional SQLite profile/rule persistence with temporary-directory integration tests.
- FG-014: added fake process-monitor simulation with grace-period proposals, protected exclusions, allow precedence, and deduplication.
- FG-015: added dependency-free TypeScript browser rule engine tests with URL normalization, privacy sanitization, precedence, and fail-open behavior.
- FG-016: added local/CI restore, formatting, build, test, coverage, extension-test, and overnight-report validation automation.

## 4. Incomplete milestones

All safe overnight milestones FG-000 through FG-016 are complete. Owner-present milestones remain untouched.

## 5. Main files changed

- `src/FocusGuard.Domain/Session.cs`
- `src/FocusGuard.Domain/Rules.cs`
- `src/FocusGuard.Domain/ApplicationRuleMatcher.cs`
- `src/FocusGuard.Domain/WebsiteRuleMatcher.cs`
- `src/FocusGuard.Desktop/Styles/Tokens.axaml`
- `src/FocusGuard.Desktop/Styles/Controls.axaml`
- `src/FocusGuard.Desktop/MainWindow.axaml`
- `src/FocusGuard.Desktop/FocusViewModel.cs`
- `tests/FocusGuard.Desktop.Tests/`
- `src/FocusGuard.Desktop/ActiveFocusViewModel.cs`
- `src/FocusGuard.Desktop/ActiveFocusView.axaml`
- `tests/FocusGuard.Desktop.Tests/ActiveFocusViewModelTests.cs`
- `src/FocusGuard.Desktop/ApplicationRuleEditorViewModel.cs`
- `src/FocusGuard.Desktop/ApplicationRulesView.axaml`
- `tests/FocusGuard.Desktop.Tests/ApplicationRuleEditorViewModelTests.cs`
- `src/FocusGuard.Desktop/WebsiteRuleEditorViewModel.cs`
- `src/FocusGuard.Desktop/WebsiteRulesView.axaml`
- `tests/FocusGuard.Desktop.Tests/WebsiteRuleEditorViewModelTests.cs`
- `src/FocusGuard.Desktop/StrictConfirmationViewModel.cs`
- `src/FocusGuard.Desktop/StrictConfirmationView.axaml`
- `tests/FocusGuard.Desktop.Tests/StrictConfirmationViewModelTests.cs`
- `src/FocusGuard.Contracts/IpcContracts.cs`
- `tests/FocusGuard.Contracts.Tests/IpcProtocolTests.cs`
- `src/FocusGuard.Service/InMemoryFocusService.cs`
- `tests/FocusGuard.Service.Tests/InMemoryFocusServiceTests.cs`
- `src/FocusGuard.Storage/SqliteProfileStore.cs`
- `tests/FocusGuard.Storage.Tests/SqliteProfileStoreTests.cs`
- `src/FocusGuard.Monitoring/SimulatedProcessMonitor.cs`
- `tests/FocusGuard.Monitoring.Tests/SimulatedProcessMonitorTests.cs`
- `extension/src/rule-engine.ts`
- `extension/test/rule-engine.test.ts`
- `scripts/ci.ps1`, `scripts/format.ps1`, `scripts/test.ps1`, and `scripts/verify-report.ps1`
- `.github/workflows/ci.yml`
- `tests/FocusGuard.Domain.Tests/SessionTests.cs`
- `tests/FocusGuard.Domain.Tests/RuleTests.cs`
- `tests/FocusGuard.Domain.Tests/ApplicationRuleMatcherTests.cs`
- `tests/FocusGuard.Domain.Tests/WebsiteRuleMatcherTests.cs`
- `TASKS.md`, `OVERNIGHT_PROGRESS.md`, and `docs/progress/`

## 6. Commits

None. The supplied directory is not a Git worktree (`git status` reports “not a git repository”), so no commit hashes can be recorded.

## 7. Architectural decisions

Validated factory methods preserve immutable records; matching APIs accept snapshots and supplied hashes; expiration is a pure UTC operation and remains independent of Avalonia or Windows APIs.

## 8. Assumptions

Application paths are compared case-insensitively with normalized separators. Website matching strips query/fragment data and treats unmatched URLs as allowed. These choices follow the privacy and fail-open guidance in the specifications.

## 9. Problems and resolutions

The shell did not resolve `dotnet`; the installed SDK at `C:\Program Files\dotnet\dotnet.exe` was used directly. One idempotence test initially found a copied record instance; `TransitionTo` now returns the original instance for same-state transitions.

## 10. Pending risks

Real enforcement, service restart reconciliation, persistence integrity, extension heartbeat, and recovery flows remain unimplemented and must not be claimed as functional.

## 11. Deliberately unexecuted for safety

No process termination, service installation, registry/hosts/firewall changes, browser policy installation, persistence, elevation, or security-tool interference.

## 12. Recommended next three steps

1. Review the completed safe queue and ensure owner-present milestones remain gated.
2. Review the real process termination design (FG-100) with explicit recovery criteria.
3. Keep real process/service/browser integration disabled pending owner-present review.

## 13. Next command

```powershell
pwsh .\scripts\ci.ps1
```
