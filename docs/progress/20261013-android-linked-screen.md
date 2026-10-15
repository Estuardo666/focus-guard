# Android linked screen — independent follow-up

User authorized continuing a step independent of the paused inverse two-Windows physical test. Scope: remove the duplicate linked paragraph visible in the owner's Samsung photo and name the linked-state refresh action accurately.

Cause: MainActivity renders identical status_pairing_linked strings in accountState and status. The same refresh button retains pending-approval copy after the link completes.

Plan: add an instrumented UI regression on the real activity with synthetic presentation state, observe failure, hide duplicate/empty account summary, switch refresh copy only for linked state, build/lint/run all Android tests and publish versioned alpha.3 with unchanged package/signing/vault. No identity/API/enforcement changes. Two-Windows inverse acceptance remains paused. Product UI test reads presentation only and never presses account buttons or submits requests.

## Result

- MainActivity hides only an empty summary or one identical to the current live status. The linked message now appears once; the verified status remains accessible. Linked refresh uses «Actualizar vínculo», while Pending/Approved keep the approval wording.
- The real-Activity regression initially failed `expected 1, actual 2`, then passed. It also checks that returning to Pending restores the approval button label. Final Android `lintDebug connectedDebugAndroidTest`: **8/8**, build successful on AVD Android15/API35.
- Initial attempts were blocked by Windows commit-memory error1455, then an absent emulator, then insufficient Gradle Metaspace256MiB. The own emulator was restarted preserving data. Only the identified failed Gradle daemon was stopped after it could not exit gracefully. Successful invocation used one worker and temporary process JVM settings `-Xms64m -Xmx1024m -XX:MaxMetaspaceSize=512m`; no OS/pagefile settings changed.
- Built versionCode3 / `0.2.0-alpha.3`. APK `artifacts/cloud-phase0/android-pairing/focusguard-android-phase2-alpha.3.apk`, SHA256 `7561FA6BA14AA43B58C8C02989E0D369437B01BF57F271196BC2A8DB157A6D89`. apksigner verifies certificate SHA256 `40c122bfc3cd1f111c976e54a8281aa8b0938cc7ac53b60b7963c92ddf7f0c9c`, same as alpha.2; update in place preserves identity/vault.
- Windows .158 and cloud API .00016-gc7 were not changed by this presentation fix. No backend deployment or account mutation. The UI test uses synthetic presentation state; actual Samsung installation of this cosmetic update is optional and not claimed as verified here.
- `git diff --check` completed without whitespace errors (existing LF/CRLF notices only). AST graph updated: 20,777 nodes / 113,500 edges. The owned headless emulator was stopped after validation to release its memory; no global ADB shutdown or unrelated process termination.

Status: independent step complete. Inverse physical transfer Windows→brother remains pending; this fix does not close Phase2 or remove the separate artifact-cleanup blocker.
