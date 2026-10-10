# FG-167 — Shared profiles, preferences and journal

## Scope and execution location

Continue Phase 1 with profile/rule/preset workflows and user preferences/journal. Work in the existing D:/TEMP/focusguard-fg166-validation copy: E: still reports Full Repair Needed. Compare current D:/E: source hashes before work and before synchronization. Preserve prior changes; no disk repair, installation, privilege, service/IPC/enforcement changes or React/WebView2.

## Plan

1. Freeze a task-local source hash baseline and copies of affected Desktop files. Reconcile only the build-generated Setup icon from E:; 344 source/build/assets files otherwise match.
2. Extract preferences/enums/resolver/store and SessionIntention/SessionJournalStore into Client, preserving JSON fields/defaults/paths/limits. Keep Desktop commands, events and view labels in Desktop.
3. Add ProfileWorkflow for profile creation/rename/duplicate (fresh rule IDs and copied cycle settings), name policies, rule updates/removal and existing profile/preset storage boundary. Use existing Domain validation and Storage behavior; keep UI active-session guards and presentation/localization unchanged.
4. Add SessionJournalWorkflow for accepted-session preferences/intentions, observed-session journal fallback and reflections. Keep repeat presentation/strict activation and current phase logic in Avalonia/backend.
5. Adapt Avalonia and existing tests/imports. Add meaningful real-SQLite, legacy JSON, clone/rule/preset isolation and journal/accepted-preference tests; no fake production data.
6. Scope format; build/full tests; isolated extension tests; regenerate and verify standalone 0.1.17 and packaged Client/Desktop hashes in D:. Refresh graphify after synchronizing reviewed source to E: when readable/writable; record any storage limitations honestly.
7. Synchronize only task-owned files after baseline guards; update task/migration/architecture/checkpoint. Do not change or overwrite unrelated E: work.

## Acceptance and risk

Same serialized settings/journal, same rule meaning/IDs (except existing duplicate behavior), immutable built-in presets and stored cycle rules. Client remains free of Avalonia/Desktop/Win32/enforcement dependencies. Backend source hashes must stay unchanged. Active-session editing lock, consent and fresh strict review remain intact. HIGH RISK where accepted-session state and cloned rule snapshots cross boundaries; existing regression suites remain release gates. Installer validation is read-only; no installed/reboot claim.
