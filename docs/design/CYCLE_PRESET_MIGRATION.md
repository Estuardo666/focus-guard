# Cycle preset migration — execution contract

Preset editing remains a separate workstream from the resolved break-exception behavior. ADR 0014 records that existing Block-only exceptions stay blocked during breaks; preset changes must preserve that profile data. Follow FOCUSGUARD_DESIGN.md and its five requested skills.

Observed source: SqliteProfileStore.SaveCyclePresetAsync is an unconditional upsert; built-in rows are update-protected by SQL, custom rows are not revisioned. Desktop FocusViewModel has two direct save paths; shared ProfileWorkflow delegates raw saves. ProfileCycleSettings.DefaultPresetId references CyclePresets with ON DELETE SET NULL. There is no public deletion workflow. Do not expose deletion that silently resets a profile default.

## Ordered implementation

1. Add versioned custom-preset storage: coherent read and canonical revision over id/name/builtin/configuration. Validate whole-second durations, mutually exclusive termination and the domain plan limit. Built-ins remain immutable; duplication creates a new custom id. Compare/create/update/delete under an immediate SQLite writer transaction. Distinct Conflict/BuiltIn/NameConflict/Referenced results. No automatic overwrite or replay.
2. Migrate both interactive classic saves and shared client workflow to the conditional interface. Keep raw compatibility/import API, but do not claim it prevents arbitrary unconditional writers. Test independently loaded classic/React revisions in both directions, deleted/recreated identity, duplicate names, built-in protection and referenced deletion refusal.
3. Add exact typed native preset detail/create/update/duplicate/delete commands: read authorization, pilot mutation capability, fresh inactive/terminal service status, document nonce, rate/deadline/exclusive gate, bounded operation ids. No paths/table names/builtin flags or arbitrary policy input from JavaScript. Known conflict reloads once; ambiguous result never retries.
4. React: saved preset selector retains its location. A separate Manage presets action opens a focused editor; built-ins labelled read-only with Duplicate. Name and focus/break/frequency/count-or-total fields with units, inline validation,44px targets. Distinct save/delete review; default Back/Escape and restored focus. Source-only preset configuration preview cannot claim a session deadline; actual session review still comes from the service.
5. Rehydrate saved preset pages and selection after writes; invalidate any prepared timer/cycle review. Preserve custom selection off-page with a separately validated detail response. Explain conflicts and references, keep existing profile defaults/data intact.
6. Verify SQLite transactional behavior, native request/authorization/response bounds, wire correlation/no replay, DOM keyboard/read-only/stale/active guards, final production dark/light720/forced-colors/reduced-motion/CSP screenshots. Regenerate and validate the standalone package for each shipped milestone. Installed parity remains a separate gate.

## Evidence needed for acceptance

- Two stores loaded the same revision; one commits and the stale writer loses without changing the winner.
- Built-in mutation is denied, duplicate gets new id and custom status.
- Referenced deletion returns a known refusal and leaves preset/default references unchanged.
- A write confirmed by one client is visible to another; old prepared session snapshots retain their service-issued endpoints.
- React writes only typed native messages, never SQLite or service IPC directly.

| Before | After | Why |
| --- | --- | --- |
| Custom fields cannot be saved from React | Named custom preset editing beside the selector | reusable session configuration without another crowded page |
| Unconditional concurrent saves | Revisioned writes with explicit conflict | preserves edits from another window |
| Built-in identity could look editable | Read-only built-in plus duplicate | predictable defaults without losing customization |
| Foreign key silently clears a default on deletion | Refuse referenced deletion and explain it | prevents an unintended profile configuration change |
