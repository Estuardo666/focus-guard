# FG-173 — Reviewable native client identity decision

## Requirement
LocalService must authorize actual Desktop/Agent/NativeMessaging executable + managed entrypoint hashes, owner SID/logon/session and operation. No identity fields from payload are trusted. Identification tokens are retained; the authoritative service remains LocalService. Existing caller ACLs and shipped paths are not yet hardened.

## Verified evidence
FG-112 recorded OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) denial for interactive user processes under LocalService. Current native observer uses exactly this right to query native image metadata, under the service identity after pipe identification has reverted. Pipe token capture cannot supply an executable hash. Current GUID tests prove capture under the same user, not LocalService compatibility. Security review confirmed this is an OS process-object access boundary, not a C# transport bug.

## Proposed choice A — narrowly scoped per-process metadata consent
Extend trusted-client startup consent to allow LocalService exactly PROCESS_QUERY_LIMITED_INFORMATION (0x1000) on that client's process object. Preserve all existing DACL rules/denies; no account/group changes, no service escalation, no token/memory/write/terminate permissions and no grant for other processes. Grant is only for the lifetime of the consented client process; manifest binary hashes/owner/scope/role remain mandatory, so granting access does not authenticate an arbitrary process. Explicit owner consent required and documented in protected installation policy.

Implementation boundaries: Desktop, ProcessAgent and NativeMessaging call a native current-process-only helper after protected policy verifies consent; future Host uses the same exact helper. Unconsented installations fail closed with a native explanation and retain offline recovery. No global process-security changes. No fallback to payload-declared image paths. Tests need a real LocalService isolated child capture before release. Current non-elevated environment cannot install/run that service probe without separate owner-present authorization; no such probe is executed.

## Choice B — OS package identity
Package each role with separately verifiable OS package identity; read package SID via identification token and verify role mapping. This requires packaging/deployment changes and native token proof; AppContainer identity alone cannot authenticate an executable. It is an architectural expansion over the existing standalone clients and installer.

## Recommendation
A preserves the current unpackaged architecture with minimal readonly metadata permission. The requested Named Pipe ACL hardening does not itself authorize changing client process-object DACLs. Obtain explicit decision before adding/executing this new consent protocol; do not silently mark Phase4 complete or broaden Windows service privilege. Host creation and all nondependent parser/role/deployment work continue meanwhile.

## Existing completed foundation
Typed role/operation allowlists, owner/logon/session matching, bounded strict protected manifest loader, exe+managed hashes, SID/logon native capture, SCM server proof/recheck, protected ancestors/files, exact pipe mask without create-instance, remote-client rejection and first-instance protection. At least 56 foundation tests pass; integration and LocalService compatibility remain missing.

## Owner decision received
The user explicitly authorized the 0x1000 consultation permission in the active goal update. The per-current-process helper and protected-manifest consent flag may be implemented; this does not authorize service installation, UAC automation or accounts/other-process DACL changes. Native current-process tests add exactly LocalService QUERY_LIMITED then restore the original ACL. End-to-end LocalService compatibility remains a separate required proof.
