# FG178 — Installed manifest root mismatch

Owner reports actual startup InvalidDataException IPC service deployment denied; readonly service query confirms Stopped LocalService. No restart/retry/installation performed.

## Root cause and fix
Manifest comparison used Path.GetFullPath but retained trailing separators, so canonical installer directory differed from AppContext.BaseDirectory. Three regression cases failed before fix (two parse failures, one noncanonical returned root). Shared IpcDirectoryPath.Normalize requires fully-qualified paths, resolves lexical full path and removes only trailing directory separators beyond filesystem root length. Both comparison operands normalized; returned manifest.InstallRoot and authorization policy root canonicalized. Protected image parent comparison also normalizes without reducing C:\ to C:. Hash, schema, owner, role, ACL and recovery behavior unchanged.

## Evidence / tests
631 full .NET tests,70 native cases,19 extension and TypeScript passed. Seven added cases: manifest/expected separator combinations, relative and sibling mismatch denial, drive-root/repeated separators + downstream auth, lexical equivalent manifest root canonicalized. Production solution build0 warnings/errors, scoped formatter.
Read-only GPT6Luna xhigh review confirmed downstream consistency and identified remaining drive-root TrimEnd edge case, corrected. Reviewer made no file/service changes.

## Packaging
Regenerated self-contained production0.1.28 after recursive scratch targets verified inside D workspace; prior artifacts retained. Setup self-test/graphical smoke passed; four embedded SDK copies match published bytes. Child-only RunAsInvoker readonly Setup paths restored. Four published host smokes (local,missing runtime,simulated recovery,genuine renderer crash) exit0; no installed endpoint. Browser packages/static installer/owner-provisioning fixtures passed.
0.1.27 invalidated for retry in artifacts/fg178/0.1.27-invalidated.txt. No claims that earlier package self-test proved installed startup. Service remains stopped; corrected deployment not yet installed.

## Receipts
- --package-self-test: PASS
- --smoke-test: PASS
- publish\FocusGuard.Service\FocusGuard.Ipc.Windows.dll: 153577AED56983CE4952BC73858BF186B9C10018200CD9C39B07E1C2461BB8C3
- publish\FocusGuard.Desktop\FocusGuard.Ipc.Windows.dll: 153577AED56983CE4952BC73858BF186B9C10018200CD9C39B07E1C2461BB8C3
- publish\FocusGuard.ProcessAgent\FocusGuard.Ipc.Windows.dll: 153577AED56983CE4952BC73858BF186B9C10018200CD9C39B07E1C2461BB8C3
- publish\FocusGuard.Host.Windows\FocusGuard.Ipc.Windows.dll: 153577AED56983CE4952BC73858BF186B9C10018200CD9C39B07E1C2461BB8C3
- --smoke: 0
- --smoke-runtime-missing: 0
- --smoke-renderer-failure: 0
- --smoke-renderer-crash: 0
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.28-Setup.exe: 4859F9A6ADD995FE0A1AC54814223227D433D17C469112679D804A5844C58303
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.28-win-x64.zip: CD77DDD7412EA3C3248DB567D527E1C7B755E848CC3E8BAB75BB5D429DC92DA4

## Files and implications
New directory helper; Manifest/Authorization/ProtectedIpcDeployment, manifest tests; version metadata and task/architecture/migration/progress. Exact canonical equality retained (no prefix matching); relative/sibling rejection and native final-path/reparse protections intact. No business rules or process metadata ACL scope change. No service/UAC/account/registry/machine ACL operations.

## Remaining / sync
Full goal remains active: retry ONLY corrected owner-present installation0.1.28 when authorized/human performed, then verify real LocalService trusted/denied client/rollback/live host status gate. Current service stopped must not be silently restarted. Source D NTFS; guarded E old-hash backup/copy/verification and graph refresh. No commits/staging or generated icon sync. Final receipt below.

Final receipt:11 own paths old-hash checked/backed up/copied/hash verified; scoped diff passed. D graph3441/5770/464, original graph refreshed. Corrected0.1.28 fresh native SDK embedded;0.1.27 retry invalidated. Service remains stopped without any machine operation by the agent; installed gate unproven, full objective active.
