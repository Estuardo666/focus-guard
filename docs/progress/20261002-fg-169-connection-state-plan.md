# FG-169 — Truthful disconnected session state

## Scope and root cause

Continue the migration with connection state independent of session state. Existing RefreshAsync catches transport failures and sets IsActive=false while retaining other fields; CanStart/CanStop/CanConfirmStrict do not require a fresh status, and protection booleans can retain old readiness. A disconnected UI can therefore imply idle or confirmed protection without evidence.

## Plan and acceptance

1. Freeze task-local hashes and affected files. Work in D: while E: is absent; preserve unsynced FG-168 changes and source receipts.
2. Add UI-independent connection/snapshot tracker in Client: unknown initially, authoritative ready status replaces snapshot, failure preserves last status/time and marks disconnected. Caller cancellation is not a lost service connection.
3. Adapt Avalonia: preserve active/strict/phase/time on read failure, show localized stale/unknown status, suppress claims of confirmed protection, disable start/confirm/stop/skip and relevant configuration/schedule mutations until a fresh ready status. Keep retry/read and recovery directions available.
4. Reconnect applies the actual backend snapshot. Only a fresh terminal status emits SessionEnded; transport failures and elapsed local time never expire or release sessions. Supplemental metrics failures remain separate.
5. Add regression tests for active/idle/initial/strict/cycle/cancel/reconnect/terminal cases and a shared tracker test. Run old suites, scoped format/build, .NET/extension checks and read-only installer 0.1.19 verification. Keep all backend/contract/persistence hashes unchanged.
6. Update graph/docs/tasks, pending source-sync manifest and checkpoint. No disk repair, installed service change, elevation, HTTP server or new frontend technology.

HIGH RISK: user-facing state must never authorize a stale strict mutation or claim release/protection on connection failure. Existing backend controls remain authoritative; tests are not installed/reboot evidence.
