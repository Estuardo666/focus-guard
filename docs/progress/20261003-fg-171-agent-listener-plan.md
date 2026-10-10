# FG-171 — Phase 3A: ProcessAgent listener availability

HIGH RISK, bounded vertical slice. Hypothesis: a sequential listener uses only the host cancellation token, so idle/partial clients hold its sole slot forever; InvalidDataException from framing escapes the IOException-only catch and faults BackgroundService (production Generic Host stops on background worker failure).

Reproduce with actual isolated named pipes using unchanged frame format and ACL. Add only an optional pipe-name test seam before recording failing regression tests. Then impose a five-second per-connected-request deadline across read, status/ack/audit and write, isolate expected malformed/disconnected/timed-out clients, and retain graceful shutdown. Keep sequential bounded concurrency, heartbeat validity, policy integrity, first-beat cycle redistribution and authority unchanged. No retry/replay of audits or session actions.

Tests: real heartbeat/ack/audit, idle/partial/truncated/oversized/malformed clients followed by a healthy client, cancellation while accepting and reading, bounded writes. Re-run all existing strict/recovery/cycle/channel tests, .NET solution and extension checks. Build/validate standalone 0.1.21 without installation; synchronize owned files against original hashes.

Out of scope: caller roles/ACL changes, client receiver lease clearing, hardcoded development client pipe, new host/React, installed blocking/reboot/recovery. These remain Phase 3B/4 gates. Core, matchers, credentials, persistence and browser behavior unchanged. Native IPC tests use GUID pipe names, never installed endpoints.

Investigate skill: healthy preamble, no freeze hook available; scope explicitly limited to this listener, its tests and required release docs/metadata.
