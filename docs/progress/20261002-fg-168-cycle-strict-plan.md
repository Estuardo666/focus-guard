# FG-168 — Shared cycle and strict activation coordination

## Scope

Finish the next Phase 1 milestone: move cycle preparation/snapshot mapping, prepared activation, strict timer snapshot and normal stop/skip requests into Client. Avalonia retains deliberate strict review, recovery-code presentation, pending preparation display/cancel, local feedback and accepted configuration. No engine, persistence, recovery, clock, IPC server, authentication or policy-consumer change.

E: is currently absent. Work and validate in the existing healthy D:/TEMP/focusguard-fg166-validation copy. Record source hashes before edits; synchronize only if the original checkout returns and matches baseline. Do not repair storage, elevate, install or change accounts.

## Plan

1. Read contracts, service handshake/recovery tests and actual review code; record a baseline. Preserve existing differences from older written specs rather than silently changing confirmations.
2. Add typed reviewed-cycle and strict-timer inputs to ISessionWorkflow. Move mapping into SessionWorkflow, preserve strict=false/true paths, rules/break settings, finite configuration, and all v2 DTOs/validation.
3. Forward prepared activation/stop/skip through Client without automatic retries, second preparation, local phase/time decisions or reload after review. Keep plaintext recovery code lifetime/cancel behavior in Desktop.
4. Adapt Avalonia with small delegates; maintain current CanConfirmStrict/recovery acknowledgement and repeat behavior. Add contract/real-pipe handshake tests plus snapshot, credential, error/cancellation/rejection tests.
5. Run explicit scoped format, Release build, full .NET and extension suites. Build/read-only validate standalone 0.1.18, matching embedded Desktop/Client hashes; no installation. Update graph, docs/task/checkpoint and source-copy/sync receipts.

## HIGH RISK acceptance

Review and activation must use the same backend preparation ID/snapshot; service owns TTL and consume-once. No strict activation from a plain Start click or skip/stop bypass. Display code never enters request DTOs/logs/storage; recovery credential remains the existing hash. Existing strict persistence/recovery/cycle suites pass and backend source hashes remain unchanged. Do not claim installed/reboot enforcement verification from in-process fixtures.
