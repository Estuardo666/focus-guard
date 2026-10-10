# FG-176 — Authenticated production wiring, pending installed gate

Status: source/package milestone verified; full goal not achieved. Metadata0x1000 consent persists. No installation/UAC/account change executed. Source is copied to guarded E checkout after verification.

## Changes
Contracts expose platform-neutral IPipeConnectionFactory/IIpcServerSecurity ports. Client retains no Windows/Avalonia dependency. Production default refuses unconfigured installed endpoints; developer defaults refuse production names. Private test namespace remains isolated. Desktop, Agent, NativeMessaging, Diagnostic and Host production composition install native verified factories: protected own owner/role/EXE+DLL proof, consented lifetime current-process query ACE, native specific-right pipe connection, SCM/native PID/session0/own-process/config/protected-service-hash verification before bytes.

Service production composition loads protected policy once and supplies same security context to all endpoints. Frame validation/deadline precedes native caller authorization; authorization precedes session dispatch, heartbeat/ack or audit/capture. Native PID/identification/logon/session/protected images/role required. Owner session remains bound while authenticated client process handles remain alive; after all die, the same explicitly provisioned owner can rebind a fresh logon. Process cache bounded32 and disposed on service shutdown.

Endpoint maintains next namespace instance before processing/disposal. First-instance creation protects initial name; remote rejection on every native instance. Control bound16+one accept, Agent/browser sequential+one reserved accept. Connect-then-close failures replace pending stream while previous namespace exists. Shutdown disposes pending streams and drains bounded requests. Backend session, strict/recovery/matchers remain unchanged.

Installer adds native helpers and Diagnostic payload. Resolves owner SID from actual interactive Windows session through WTS (not elevated installer user), uses that loaded user hive for Run/native registrations. Requires explicit metadata consent; Setup shows readonly permission explanation/defaultNo before closing/replacing processes. Protects full install directory/files to System/Admin full +Users/LocalService RX, removes inherited/untrusted grants and assigns Admin ownership before generating strict policy, repeats for generated file. Protected owner manifest includes EXE+managed hashes and explicit Host/Diagnostic roles. Existing unexpected service account is refused, not changed; config quoted and original config/files/per-item ACLs restored on rollback. Owner-context health probe launched through interactive Shell and identified by exact path/starttime/held process handle. No filenames used to kill the probe. Uninstall reads installed owner policy for registry cleanup and offline recovery remains separate.

Host production status gate now uses successfully composed native deployment factory; copied/unprovisioned host stays local and unavailable. Every status exchange still verifies SCM server. Smoke modes always use no-service NotIntegrated composition. No bridge mutations/arbitrary commands, Avalonia default unchanged.

## Tests / commands
- Full default solution 624 tests passed; production 622 passed before two later channel tests.
- Three real private-pipe dispatch tests: denied stop preserves session/deadline and listener succeeds with allowed status; denied Agent/browser heartbeats do not renew leases. Test security intentionally isolates dispatch ordering; it is not a real LocalService identity proof.
- Unconfigured installed endpoint factory rejection test. Existing pure Client dependency tests pass; SDK native63 tests retained.
- Production build0 warnings/errors, scoped formatting, explicit developer/production constants assertions corrected existing dev-only test to honor build flag.
-19 extension tests and TypeScript passed. Windows PowerShell5.1 installer/provisioning fixture checks pass. WTS owner helper actually returns current interactive owner's SID without registry/service changes.
- build-installer.ps1 -Version0.1.26: self-contained production with SDK/Diagnostic/helpers; resolved recursive scratch targets verified within healthy D.
- Setup self-test/smoke and eight embedded hashes match fresh publish. Child-only RunAsInvoker readonly Setup paths restored after tests.
- Published native host local/runtime-missing/simulated renderer-recovery modes exit0, no installed pipes. Actual crash remains missing.
- Browser package checks and reboot drill parse only, no actual machine drill.

## Receipts
- --package-self-test: PASS
- --smoke-test: PASS
- publish\FocusGuard.Service\FocusGuard.Service.dll: 78E320D2147139088E1D6F5A88A5DF92D02A199968B1A7C416D67155331B7727
- publish\FocusGuard.Desktop\FocusGuard.Client.dll: ECDC08ED6DCEFFE3EB6AEC1417EDB6DED98D0FC38378549EB01C8B38F187B113
- publish\FocusGuard.Host.Windows\FocusGuard.UI.dll: E05E05C1DFA1AE8AFAE9DDCD440EA7C42B09111E687360E061ECCF40F849DB97
- publish\FocusGuard.ProcessAgent\FocusGuard.ProcessAgent.dll: 1D948F0AFAF8485DDF410F9648F77EC020B1DD97A6A89538B3FC13D5370176F2
- publish\FocusGuard.NativeMessaging\FocusGuard.NativeMessaging.dll: 418A5019E3EFA7E01AA95FFB198C7133D5567920FDAECBD56095BFCAA46F4E66
- publish\FocusGuard.Service\FocusGuard.Ipc.Windows.dll: 2A16FA7ACF07EE886203704BB70F58EF9CC3E0FF77D2DEE4820D8B3C7926F560
- New-FocusGuardIpcPolicy.ps1: D7E36E05B06CEB92ED229D27D82FAE9F3FB526218B1B4A466BCDF21366ACEC47
- Get-FocusGuardInteractiveOwnerSid.ps1: D7D71C7501565322C74C41A6E54A796FD82190A243E184AF78D5283B4B9F6745
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.26-Setup.exe: FFAA9AB717317602DE243E5CB2F5B7382918CD3ADE21F8BE12B54F881A8C7D28
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.26-win-x64.zip: 3C2F953567AFDAD511D42CB94AB60B87DE13DE1A164F562D354A4CB7971CEA7B

## Risks/remaining acceptance
Native production wiring has not yet been exercised under real LocalService against actual protected owner clients. Current token is NOT elevated; installed FocusGuard remains its previous version. A green build/test/package does not prove installation/rollback/owner-context health behavior. Do not declare Phase4 complete until owner-present protected deployment tests show legitimate Desktop/Agent/Native/Host and wrong-role/user/squatter failures plus actual runtime continuity. Native renderer crash remains unproven; interrupted Luna work archived, no unknown process killed. Offline recovery and finite authoritative expiration preserved by source/tests, actual installed recovery requires its existing drill.

Metadata consent authorizes process-specific query only, not implicit service installation/elevation/account changes. Concrete0.1.26 package is reviewable for a separately authorized owner-present install. Once approved, execution still requires already privileged context or human-owned UAC; agent must not auto-elevate.

## Sync
D NTFS builds; E repair warning. Only own paths old-hash checked/backed up/copied and verified. No commits/staging; generated Setup icon excluded. Graph receipts follow. Full objective remains active.

Final receipts:38 owned paths synced/backup/hash verified, scoped whitespace check passed. D graph3341/5535/452; E graph refreshed. Actual non-elevated context confirmed; existing LocalService remains running unchanged. Owner-present manual0.1.26 install question sent only after concrete package validation. Metadata permission already authorized, not requested again. Overall goal remains active pending installed and genuine renderer crash gates.
