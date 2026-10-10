# FG-175 — Control listener shutdown + consented native foundation

Status: listener milestone verified; full active goal remains IN_PROGRESS. User explicitly approved metadata0x1000 in goal update. No repeated permission request; authorization retained. No service installation/UAC/account/other-process ACL changes authorized or performed.

## Fix and evidence
FocusControlPipeService created a pending NamedPipeServerStream and did not dispose it when cancellation interrupted WaitForConnectionAsync. Isolated GUID regression connected successfully after StopAsync and failed before fix. Pending stream ownership now tracked, transferred to connected handler only on connection, disposed in finally before draining tasks. Regression now times out after stop and successfully rebinds with a new listener. Additional incomplete-header shutdown test preserves exact active session ID/expiry/state. No session engine, IPC wire, strict/recovery or other channel semantics changed.

## Native foundation progress
Actual restricted-token test proves Win32 access denied5 against prepared pipe DACL, ordinary-token exchanges before/after and reversion. Restricted token is a reducing-rights fixture, not real second-user deployment.
Owner-approved CurrentProcessMetadataConsent adds exactly LocalService query0x1000 on current process, keeps all original ACEs/denies, refuses absent consent, reference-counts overlapping clients and restores original DACL. Three real current-test-process native cases pass. No other process touched. Protected manifest includes consent defaultfalse; helper exposes explicit switch.
AuthenticatedIpcClient/Server contexts added for strict protected deployment/role/owner proof, but distributed clients/listeners not yet wired; production F4 remains incomplete. Server path now verifies pipe PID+session0 against SCM running own-process type0x10/LocalService/exact config, protected EXE+managed hashes, then rechecks SCM/pipe metadata. Readonly probe showed client OpenProcess on actual LocalService PID denied5; no added server-process ACL required. This protects ordinary-session squatters under existing threat model, not compromised service/admin/in-memory attestation.

## Tests / validation
- Complete solution: 620 passed in 14 suites, no failures (artifacts/fg175/tests-complete.log). Service92, IPC63, Host46; legacy regression retained.
- Scoped formatter; installer script Windows PowerShell5.1 checks; policy provisioning fixtures pass;19 extension tests/TypeScript passed.
- Build-installer.ps1 -Version0.1.25 regenerated after resolved recursive targets verified within healthy D workspace. Existing releases preserved.
- Standalone self-test/Setup smoke/three embedded DLL hashes match fresh publish. Child-only RunAsInvoker used for readonly Setup tests/hashes and restored.
- Published host local-load/runtime-missing/simulated renderer recovery smokes exit0, no service calls. Actual renderer crash not proven.
- Chrome/Edge package0.1.25 checks pass.
- Luna xhigh subagents used per owner request: restricted-token proof completed; security review yielded SCM alternative; renderer probe interrupted by model usage limits. Interrupted untested host changes archived under artifacts/fg175/interrupted-host-probe and validated host restored from verified E baseline. No unknown/shared browser process killed, no model substitution.

## Package receipts
- --package-self-test: PASS
- --smoke-test: PASS
- publish\FocusGuard.Service\FocusGuard.Service.dll: AE3C1D98B9BBCB5697968DBB11FF9C5B98BC8DDBE7B246E8852831E3AC9E7AF7
- publish\FocusGuard.Host.Windows\FocusGuard.UI.dll: 5BE514BD0297D0752BD96E61B3BC746E6BBC8BCA0C76620672B5A90D029620B5
- publish\FocusGuard.ProcessAgent\FocusGuard.Service.dll: AE3C1D98B9BBCB5697968DBB11FF9C5B98BC8DDBE7B246E8852831E3AC9E7AF7
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.25-Setup.exe: EF4A82ABD0B0D93F71AC3AEE620360C356AE374B36263EACA7F2FF3C943B7DDF
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.25-win-x64.zip: A3842F363DD7A348B6493E8CB998608167F8FB1D7476FADFBB48F4E1A324BD67

## Files / UX / security / recovery
Control service + regression tests; restricted-token/native consent/SCM/context source/tests; manifest consent/helper/schema; release metadata0.1.25 and task/architecture/migration/progress docs. Existing UI/defaults remain unchanged; host still refuses real service requests before F4 native integration. Consent code is unshipped in native foundation until wiring; current deployed endpoints retain old ACLs and are not claimed hardened. Offline recovery and authoritative service expiration unchanged.

## Remaining acceptance gates
Wire protected owner/consent policy into real installer rollback and service/client startup. Client factory verification before bytes, native SID/logon/image/role verification before each dispatch, owner ACLs/namespace-first lifetime, malformed/slow/unauthorized compatibility; real LocalService→interactive client proof. Logon rebind policy must handle legitimate relogin without uncontrolled multi-session access. Actual renderer crash plus installed/deployment parity gates remain pending. Full objective NOT achieved; keep active.

## Synchronization
D NTFS build/artifacts; E checkout still repair warning. Owned paths prechecked against recorded hashes, backed up/copied-only and final verified; no commits/staging or generated icon sync. Receipts follow.

Final receipt: all24 owned paths copied against recorded old hashes and verified; scoped diff check passed. D graph3270/5401/440; original E graph5205/9234/867. Metadata consent permission received and retained; no future reapproval requested for this scope. Source authentication wiring and actual LocalService positive/negative compatibility remain incomplete, overall goal active.
