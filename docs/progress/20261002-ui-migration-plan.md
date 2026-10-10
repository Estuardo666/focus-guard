# UI migration planning — 2026-10-02

## Authorized scope and work plan

Produce `docs/FOCUSGUARD_UI_MIGRATION_PLAN.md` from the current working tree. Inventory projects, specifications, graph report, source, IPC, enforcement, lifecycle, storage, tests and packaging; run existing safe build/tests; verify platform guidance against official documentation; define phased migration, security, first slice and rollback. Do not implement the migration, change enforcement, install services, elevate or rebuild a shipped installer for documentation-only changes.

## Initial findings

- Configured C: checkout is absent; actual checkout is E:/FocusGuard/FocusGuard-Autopilot.
- HEAD is 7820a8a277ace810adfa180dd6a254f3dd82b823; substantial pre-existing modified/untracked work is present and must be preserved.
- Service, Domain, Contracts v2, ProcessAgent, browser extension, recovery and installer already exist.
- Main concerns: Desktop orchestration mixed with Avalonia; authenticated-user pipe ACLs without executable identity authorization; ProcessAgent timeout/lease failure handling; preserving recovery and independent enforcement channels.

## Results

Created `docs/FOCUSGUARD_UI_MIGRATION_PLAN.md`: 23 required sections, four Mermaid diagrams, actual project/dependency map, extraction targets, phased DoD, exact first slice/files/interfaces, existing v2 versus new bridge contracts, owner/role/process authorization proposal, threat model, WebView2/React/native design, tests, packaging and rollback.

### Files changed

- Added the migration plan and this checkpoint.
- Appended documentation-only completion to TASKS.md without changing prior milestones.
- No application code, tests, dependency versions, enforcement policies, installed files or machine configuration changed. Build outputs and audit TRX are under ignored artifacts/bin/obj.

### Validation

- `dotnet build FocusGuard.slnx --configuration Release --nologo`: passed, zero warnings/errors.
- `dotnet build src/FocusGuard.ProcessAgent/FocusGuard.ProcessAgent.csproj --configuration Release --nologo`: passed, zero warnings/errors. Agent is currently omitted from the solution.
- `dotnet test FocusGuard.slnx --configuration Release --no-build --nologo --logger 'trx;LogFilePrefix=migration-audit' --results-directory artifacts/migration-audit`: 398 passed, zero failed/skipped across ten suites.
- `npm run check`: passed. Concurrent `npm test`: 18/19, navigation performance median 10.580 ms failed the unchanged 10 ms budget. An isolated unchanged suite rerun passed 19/19. Both outcomes are recorded in the plan.
- Structural document checks: numbered sections 1–23, balanced fences, all proposed versus existing components distinguished. Cross-checked timer limits, policy exclusions, service registration, framing and codecs directly against source.
- No tests added: documentation-only deliverable. No formatter changes, installer regeneration or graph refresh: no shipped component or source code changed.

### Security, UX and recovery implications

Recommendations preserve LocalService, user ProcessAgent, MV3 enforcement, independent leases, immutable policies and elevated offline recovery. Proposed WebView2 host is non-elevated; explicit bridge methods and authenticated local IPC are release gates. UI status becomes stale rather than idle on disconnect. Named-pipe caller authorization and ProcessAgent deadline/lease handling need separately tested HIGH RISK work before the new real-client pilot. A protected temporary host executable name and own WebView2 process-tree review avoid broad rules accidentally blocking the new interface.

### Limitations

No installed service/browser enforcement, elevation, reboot or recovery drills were performed. OwnerPresent test reports pass without real termination unless its opt-in is enabled. Unit/in-process E2E results do not establish installed parity. Existing documentation/spec drift, DEV Agent hardcoded PROD pipe, 720-minute UI preferences versus 30-day backend, and unfinished installer/native signoffs are explicitly documented. Substantial pre-existing working-tree work remains preserved.

### Next task

Phase 1A transport extraction and bounded Phase 1B status/normal-session workflow, following the exact implementation prompt at the end of the plan. React/WebView2 and enforcement/security fixes remain subsequent scoped work; this task authorized planning only.
