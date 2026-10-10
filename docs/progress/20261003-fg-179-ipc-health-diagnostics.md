# FG179 — Installed IPC health failure diagnostics

## Outcome and evidence
User installation0.1.28 reached Running but failed authoritative owner-context probe twice, rollback completed. Extracted temp manifest confirms0.1.28; readonly queries show service Stopped LocalService, original unquoted config restored, old install tree lacking new SDK/policy. No new Application crash event during attempts; prior manifest startup crashes predate them. Do not attribute failure to missing permissions or timeout without evidence.

Observed diagnostic defect: Desktop bootstrap was outside health try; exchange failures reduced to1; service isolated UnauthorizedAccess without evidence. Root cause of actual IPC rejection is still unknown. GPT6Luna xhigh read-only reviewer confirms gaps and suggested retaining specific policy denial codes. No authorization, account, role, process rights or engine changes.

## Changes
ServiceHealthProbe catches/bootstrap11/exchange12/not-ready14, same eight concurrent authoritative status probes and authenticated Desktop factory. Owner-local fixed health-check.json overwritten with UTC, stage, fixed diagnostic code/type/HResult/native error; messages/payload/paths never serialized. Constructor now inside probe boundary; installer probe stays observable two seconds on every outcome. SDK typed internal policy exception preserves only allow-listed denial subcodes. Service writes one ipc-control-rejection.json at most once/10seconds via monotonic gate; no append-only rejection log. Installer prints size-bounded allow-listed server rejection before rollback and exit code/path hint; diagnostic failure cannot interrupt rollback. Existing normal startup unchanged. Extension version aligned0.1.29.

## Validation
642 .NET tests,19 extension tests and TypeScript; scoped dotnet format. New tests cover bootstrap failure, exchange native cause, private message redaction, known/unknown policy denial codes. Installer diagnostic extraction fixtures exercise native code/policy/UTC, corrupt JSON, size bound and unknown-code suppression. Static installer and protected policy provisioning/browser package fixtures pass. Production versioned package build and Setup package-self-test/graphical smoke0; published SDK copies match. Initial build caught Windows platform analyzer on finally Dispose (guarded); second build caught extension version mismatch (aligned). Both corrected before final packaging. Host smoke first used an invalid --smoke-test flag, exit2; correct flag is --smoke, verified separately.

An additional published Desktop bootstrap-smoke command that included owner-local diagnostic backup/restoration was rejected by automatic execution approval with only 'blocked by policy'; command did not execute. This smoke is unproven. Installed LocalService acceptance remains unproven. No install, service start, elevation, policy activation or installed-file modification performed.

## Artifact
D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.29-Setup.exe
SHA256 EA3217E610722FC55B42A99C7DAF2419298856B150EE880B4220E51B945E472F
Logs and sync receipts: artifacts/fg179/. Production package is diagnostic; no claim underlying rejection fixed.

## Next
Owner-present0.1.29 retry supplies installer exit/stage and service rejection (if any); read owner LocalAppData/FocusGuard/diagnostics/health-check.json if needed. Use exact code/native error to reproduce and implement the minimal auth/transport fix, then verify real permitted/denied clients and rollback. Never treat installation Running alone as success or bypass authentication.

Final validation: four published host smokes0 (local,missing runtime,simulated recovery,genuine renderer crash); five embedded SDK hashes match published bytes. Own source synchronization guarded by original hashes/backups; no commits/staging.
