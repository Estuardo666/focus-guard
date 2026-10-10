# FG-169 — Connection state independent of session state

Status: **DONE in validated D: source; cumulative E: synchronization recorded below**. This is a client/presentation milestone, not backend/security parity. Task started in the preceding checkpoint dated 2026-10-02; final client context is 2026-10-03.

## Problem and resulting behavior

RefreshAsync previously assigned IsActive=false on IOException/timeout/invalid status, retained stale readiness flags and left mutations without a fresh-state requirement. It could imply idle or confirmed protection although the backend session remained active. A targeted regression failed on the untouched baseline, then passed after the change; logs are `artifacts/fg169/{reproduction.log,regression.log}`.

Client now has SessionConnectionState/Snapshot/Tracker, independent of policy/session authority. Initial state is unknown. A successful ready reply replaces the confirmed snapshot and receipt time; a read failure or non-ready reply retains the last confirmed status/time and marks disconnected. Caller-requested cancellation does not invalidate good evidence. The tracker never advances phases or expires/releases sessions.

Avalonia retains active/strict/phase/deadline/remaining/objective/profile on disconnect. It shows localized stale/unknown state and avoids asserting confirmed protection or break release. Start/strict confirm/stop/skip and relevant profile/preset/schedule mutations require a fresh ready status. Read/retry and cancel-review remain available; recovery presentation is unchanged. A lost mutation response invalidates evidence and blocks another attempt until status reconciliation; no automatic replay is added.

Reconnection to the same session restores current backend time/actions without replaying closure. Only a fresh terminal status emits SessionEnded, once. Local elapsed time or disconnected polls never close the session. Supplemental activity metrics failure remains separate and leaves a fresh status fresh. Identical inactive reads still avoid re-notifying the visual tree; the snapshot timestamp updates internally without a redundant UI notification each poll.

## Files changed

- Client: new State/SessionConnectionSnapshot.cs (enum/record/tracker); SessionWorkflow status read/cancellation/error evidence and invalidation; ISessionWorkflow read-only snapshot/invalidation port.
- Desktop: new SessionConnectionViewModel.cs; FocusViewModel state preservation, action guards, uncertainty and health presentation; FocusWorkflowViewModel schedule/repeat/compact guards; RuleEditorViewModel notification fan-out for freshness.
- Resources: five EN/ES keys for checking, stale, unknown, unconfirmed protection/release and uncertain request result. Existing strings remain; callers no longer use “No session was activated” for an unknown response.
- Tests: four tracker cases, nine Desktop cases (including the baseline-failing regression), one native headless rendering/in-process service case; existing test for service unavailable now expects truthful unknown-state copy rather than the old definitive label.
- Extension: version metadata only 0.1.18 → 0.1.19; no logic/permission/identity change. Documentation/tasks and generated graph updated.

No Domain/Application/Contracts/Service/Storage/Monitoring/Infrastructure.Windows/ProcessAgent/NativeMessaging/Recovery source changed; hashes match task baseline. There is no JS bridge, WebView2/React, HTTP server, new privileged operation or installed service mutation.

## Validation

- Targeted regression: failed before fix because IsActive became false; passed after fix.
- Release build: zero warnings/errors.
- Full .NET: **456 passed, zero failed/skipped**, eleven suites. Application 4, Domain 34, Contracts 46, Infrastructure.Windows 15, Monitoring 12, NativeMessaging 23, Storage 18, Service 74, Desktop 167, Desktop.UiTests 15, Client 48.
- New cases cover initial/idle disconnect, active retention/disabled stop, same-session reconnect, confirmed terminal closure once, strict-review confirm denial with cancel still clearing code, frozen break state/no release claim/no skip, caller cancellation, uncertain activation result, profile draft persistence denial, unknown/stale tracker/time/non-ready/reconnect and visible native UI warning while the underlying in-process service stays active with the same ID/deadline.
- Existing strict/cycle/recovery/clock/channel suites and supplemental-metrics regression remain intact.
- Initial full-suite checks exposed redundant LastConfirmedStatusUtc notifications on identical inactive reads; fixed the notification fan-out rather than weakening that test. The old unavailable-copy assertion was updated intentionally because “state unknown” replaces an unsupported claim about enforcement. Final old/new suites pass.
- Explicit-file formatting verification passed. TypeScript check and isolated extension tests passed **19/19**.
- Graphify AST update in D: passed: 2,581 nodes, 4,045 edges, 335 communities. This isolated-copy corpus differs from the E: graph; no API use.

Commands/logs/TRX: `D:/TEMP/focusguard-fg166-validation/artifacts/fg169/`. Used solution Release build/test, file-scoped format, npm check/test, graphify update, installer build and existing installer/drill-parse/browser package tests. Repeated TRX runs are not summed; final log identifies one final 456-test run.

## Installer 0.1.19

Standalone:

`D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.19-Setup.exe`

SHA-256: `20FE4B6BD8D88DC5B0B741DBFBF058E15ED93C6C5595D81D44235E4B1E2237A1`

ZIP:

`D:/TEMP/focusguard-fg166-validation/artifacts/installer/FocusGuard-0.1.19-win-x64.zip`

SHA-256: `450122799DD1A0FC0B4E4E3A6A3B34008ACBBA30B336187CC524EEA4CA5DAF15`

Embedded Desktop.dll: `CDDCA25057CC45499C5D1DC47E1D539CE836D1C77EC5327081DEFC7A63714408`

Embedded Client.dll: `D9B4D98BAF6A0B3E74173C0E56B85A1CD5F9683CC8FC5228D8E945DCAF4F1331`

Standalone self-test and graphical smoke both exit 0; embedded hashes match fresh publish. Installer script, FG-112 drill parse and Chrome/Edge package validations passed. No installation/real OS blocking/reboot/elevated recovery was performed. Validation-only child RunAsInvoker setting was restored afterward; shipped requireAdministrator manifest/accounts/ACLs unchanged. Temporary extraction uses D:/TEMP.

## Storage and synchronization

Work/build/test/package ran in the existing healthy D: copy. E: was absent initially, then returned still reporting Warning/Full Repair Needed. No repair or permissions change was attempted. The cumulative manifest combines unsynced FG-168 and current FG-169, retaining each earliest OldHash against the pre-FG-168 original checkout; new paths must be absent. Guards passed and originals were backed up in `artifacts/fg169/pre-sync-backup` before copy-only synchronization.

Final source/doc hashes and receipt are checked against D: before declaring E: synchronized. Bin/obj, install artifacts and build-generated icons are excluded. No unrelated owner work is overwritten. The original external-drive storage condition remains unresolved; validated installers/logs remain on D:.

Final receipt: all 22 cumulative FG-168/169 source/doc paths synchronized and hash-verified against D:, with original backend hashes unchanged. E: graph refresh also passed: 4,744 nodes, 8,513 edges, 844 communities. Both milestones' sync-status receipts now record completion. Task-scoped `git diff --check` passed; no unrelated AXAML was reformatted.

## UX, security and recovery implications

HIGH RISK state change is deliberately conservative: loss of status or a mutation response never implies release or allows a fresh command from old evidence. Strict flags/consequences and pending review remain; confirmation is disabled until reconnection and cancel is still usable. UI-visible warning/protection text and command availability update together. Backend expiry/leases/recovery are untouched.

These in-process/native-headless tests do not establish installed/reboot protection. Previously documented pipe caller authorization, agent timeout/lease, stored Block versus prepare Allow break-rule mismatch and deployment/recovery signoffs remain separate pending tasks. No stronger anti-tamper claim is made.

## Next step

Close the platform boundary extraction (application discovery/icon rendering and native lifecycle ports) before introducing a WebView2 host. Keep IPC/agent security and the break-rule mismatch in dedicated HIGH RISK milestones before the real React pilot. Do not declare full migration/parity or install the candidate as part of this step.
