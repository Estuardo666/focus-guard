# FG-166 — Shared client and normal-session workflow

## Scope

Owner requested the next step after the migration plan. Execute Phase 1A and only the status/normal timer substep of Phase 1B. Preserve the existing working tree. No React/WebView2, enforcement changes, installed service changes, elevation or installation.

## Implementation plan

1. Record current source hashes/status. Move the existing control interface and named-pipe client/partial to FocusGuard.Client without altering v2 codecs, message types, timeout, correlation or impersonation level.
2. Add ISessionWorkflow/SessionWorkflow for authoritative status and normal activation using a freshly resolved saved profile and copied policy-rule snapshot. Preserve explicit enforcement opt-in, normal strict=false/recovery=0, backend validation and no replay on ambiguous failures.
3. Adapt Avalonia status/normal activation to the workflow; leave strict/cycle paths, journal/settings and error presentation in Desktop. Update references/usings, DrillClient and move transport tests into Client.Tests. Include existing ProcessAgent in solution build without changing its code.
4. Add meaningful SQLite/workflow and actual in-process named-pipe tests, including bounds, missing profile, cancellation, rejection and failure/no-retry. Preserve all existing suites and native Avalonia coverage.
5. Run scoped formatting, Release build, full .NET tests and isolated extension check/tests. Refresh graphify. Build and validate standalone installer 0.1.16 with fresh Client/Desktop payload and self-test/smoke; extension version metadata may change to satisfy existing coherent package-version checks, but no extension logic changes.
6. Update TASKS and completion checkpoint with files, commands, test results, hashes, UX/security/recovery implications and next scoped step.

## Risk and acceptance

Session activation extraction is HIGH RISK because rule snapshots and explicit consent must match current behavior. Backend, strict recovery, clock, cycles, matchers and privileged setup behavior remain unchanged. The newly shared library may reference Contracts/Domain/Storage, never Desktop/Avalonia/Service/Win32. Status remains authoritative and exceptions propagate rather than fabricating idle. No automatic retry of activation.

Done requires passing old/new suites, a new validated standalone installer, graph refresh and documented limits. Real installed/reboot tests and IPC hardening remain later milestones; no test fixtures imply installed enforcement verification.
