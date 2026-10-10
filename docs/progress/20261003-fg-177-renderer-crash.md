# FG177 — Genuine isolated renderer crash proof

Status: native host failure milestone verified; full goal pending installed Phase4 gate. Previous goal turn made source/package progress; no blocker status restatement substituted for work.

## Change
Fixed CLI --smoke-renderer-crash uses only its own CoreWebView2 instance, unique smoke UserDataFolder, denied IPC composition and bounded20s lifetime. Calls native DevTools Page.crash with constant empty parameters. No JS message/setting/PID/path controls this API, no process-name lookup/termination, no installed endpoint. Actual ProcessFailed RenderProcessExited sets proof flag; success only occurs after a new local navigation/document token and AuthorizationUnavailable response. Synthetic callbacks are not used by this scenario. Normal production path never invokes diagnostic command.

## Evidence
Release host build0warnings/errors, initial native crash smoke exit0. Complete solution624 tests,19 extension tests and TypeScript pass, scoped formatter. Production standalone0.1.27 rebuilt after safe resolved scratch paths; published --smoke/--smoke-runtime-missing/--smoke-renderer-failure/--smoke-renderer-crash all exit0. Setup self-test/visual smoke and embedded Host/Service hashes match publish; readonly child RunAsInvoker restored. Browser packages/static installer checks pass. Existing service observed Running LocalService unchanged. No installation or privilege change.

CDP reference: https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-crash (native renderer diagnostic, not arbitrary JavaScript bridge).

## Receipts
- --package-self-test: PASS
- --smoke-test: PASS
- publish\FocusGuard.Host.Windows\FocusGuard.UI.dll: 8BCD2CF43A83D862A471DA56C4D12B390644D61487C4AA561E74ABD5B3BD3202
- publish\FocusGuard.Service\FocusGuard.Service.dll: 78E320D2147139088E1D6F5A88A5DF92D02A199968B1A7C416D67155331B7727
- --smoke: 0
- --smoke-runtime-missing: 0
- --smoke-renderer-failure: 0
- --smoke-renderer-crash: 0
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.27-Setup.exe: 9DE03AF1F3F52740BBF1D6A2E8BAE14C4DDD64BACE380BF55DA9905BB65FDFAE
- D:\TEMP\focusguard-fg166-validation\artifacts\installer\FocusGuard-0.1.27-win-x64.zip: E6D78AC91223880C18C7F9C24A2FDFCC8807F18632167A07A265F3EFA4BDA1AE

## Limits and next
This proves actual renderer crash/recreation for isolated Evergreen component, not installed authenticated IPC or whole-machine reboot/recovery. Phase3B done, Phase4 source/package wired but actual LocalService legitimate/denied client/rollback gate awaits owner-present installation. Phase5 local-host/runtime/renderer/bridge constraints covered; live authorized native status must still be checked with installed Phase4. Root does not auto-elevate or infer service-install approval from0x1000 consent. Overall goal active.

## Files/sync
Host MainForm/Program/HostComposition; version metadata and task/architecture/migration/progress notes. D build/artifacts; E guarded old-hash copy/backups, no commits/staging or icon sync. Graph and final receipts below.

Final sync receipt:9 own paths old-hash compared/backed up/copied/verified; scoped diff passed. D graph3398/5665/440; E graph refreshed. Actual crash milestone complete, full goal active pending installed native authentication gate.
