# FG-173 — Phase 4 progress (not complete)

The previous goal turn made verified progress: Phase3B source/package/sync finished; Phase4 native policy code started. Current continuation added evidence-driven native and policy validation, not a narrower replacement objective. Full active goal includes Phase3B, remaining lifecycle review, Phase4 and Phase5.

## Changes
New FocusGuard.Ipc.Windows library and tests in solution. Typed per-role request allowlists; exact owner SID/logon/session proof; native pipe PID and TokenLogonSid capture with Identification; held process handle/time/path under reverted service identity; strict protected ipc-policy.json loader bounded to 64KiB, schema1/no unknown or duplicate fields; apphost plus managed DLL hashes; protected file/root/ancestor ACL checks; kernel final file path comparison; SCM running PID/account/image/config/hash check with re-read; native local streams with exact0x12019b rights excluding CreateNewInstance, REJECT_REMOTE_CLIENTS and first-instance flag. Native evidence creation/mutation internal, unavailable to service JSON callers.

Central WebView2 package1.0.4258.31 verified through official NuGet feed for independently delegated Phase5. No new library is referenced by shipped Service/Client/Agent paths yet; current production endpoints remain unchanged and are not claimed secure.

## Validation
At this point 57 new IPC cases passed: role boundaries, NativeMessaging status compatibility, wrong owner/logon/session/hash/temp copy/writable root/file, manifest size/unknown/duplicate/schema/managed hashes, tampered DLL despite same apphost, server SCM/PID/account/path/hash/config mismatch, real Identification pipe capture despite spoofed payload pid/SID, real readonly SCM RpcSs query, real native local stream exchange, duplicate first-instance rejection, ACL client mask, Temp denial and actual ProgramFiles ancestry positive test.
Earlier complete solution run passed509 existing plus44 foundation cases; subsequent focused57 pass. Final full suite will be rerun after delegated integration. Formatting scoped; graph updated in validation workspace. No native process/installation/account/service/registry ACL change performed; tests create ephemeral GUID pipes only.

## Corrections from native evidence/security subagent
WindowsIdentity.Groups omits logon SID; use GetTokenInformation(TokenLogonSid28), proved real. Empty default pipe buffer initially stalled test WriteAsync before read; concurrent bounded read fixed test setup. Ancestors must allow harmless sibling creation, accept TrustedInstaller owner, and forbid replacement/security mutation; full write denial remains on installation root/files. Normal NativeMessaging status message added. OS SCM query can capture other Windows service arguments, but trusted FocusGuard match rejects unexpected arguments. Native final-path handle proof added. Apphost alone insufficient: managed entrypoint hash now bound.

## Required unresolved decision
Security review confirmed LocalService needs process object access to query actual interactive client image; Identification token supplies owner/session, not code image/hash. Existing Windows clients are unpackaged. The reviewable alternatives in 20261003-fg-173-client-identity-decision.md are explicit per-client lifetime readonly metadata consent (0x1000 to LocalService only) or separately authenticated package identities. User async question sent; no answer or approval assumed. No metadata ACE has been added/implemented/executed. Native observer currently fails closed if OpenProcess is denied.

## Remaining work
Implement the selected authorized identity protocol; explicit owner provisioning in real Setup/install/rollback; protected manifest validation in all clients/listeners; all named pipe native authorization before dispatch/send, first-instance namespace lifetime ownership, owner-specific exact masks and remote rejection, endpoint fault/deadline/regression gates. Real LocalService-to-interactive compatible trusted and denied clients, fake server/remote owner tests required. A pure policy test is not completion evidence for installed integration.

Phase5 delegated to GPT-6 Luna xhigh and proceeding independently, with default bridge gate closed and no installed endpoints in smoke. Owner helper delegated separately, reviewed atomic generation schema/tests pending integration.

## Packaging and synchronization
Latest distributed milestone is verified standalone0.1.22 from Phase3B. Current Phase4 library not shipped; no updated package claim until integration. Work in D NTFS due E volume Warning/Full Repair Needed. FG173 owned source sync is staged next; new host files remain agent-owned until report. Existing FG172 code/docs preserved.

Final combined evidence:57 IPC +46 host +509 legacy =612 tests passed; all19 extension passed. Host packaged as dormant0.1.24 with native tests and limited bridge. No new process-object ACE; the explicit decision remains unanswered. Source sync/graph receipt in FG174 checkpoint.

Update: owner authorized query0x1000 explicitly. CurrentProcessMetadataConsent implemented/tested with exact current-process grant and DACL restoration, no absent-consent mutation. Protected manifest carries clientMetadataQueryConsent defaultfalse. AuthenticatedIpcClient/Server composition contexts now implemented but not yet referenced by shipped transports/listeners. SCM+native pipe PID/session0/own-process service+protected disk exe/DLL proof removes need to grant the user query access to the service process; ordinary session squatter rejected. Live LocalService compatible positive/client role tests remain missing.
