# FG179 — Diagnose installed IPC health failure

0.1.28 extraction confirmed; authoritative installer probe failed twice, rollback restored old deployment and service is stopped. No new Application crash event; exact IPC failure cause unavailable: exchange errors are discarded client/server side. Do not infer a permission/timeout fix.

Implement bounded, sanitized diagnostics: client health probe reports bootstrap/exchange/readiness separately and preserves inner native error; server records first control authorization rejection in one rate-limited snapshot. Preserve eight concurrent status probes, all authorization checks and rollback. Installer reports exit code and diagnostic locations. Test redaction/native causes/bootstrap failure, format/build/test, produce diagnostic0.1.29 package, verify readonly self-tests; no install/start/elevation. Then real owner-present retry supplies missing evidence. No sessions or policy payloads in diagnostic records; no append-only rejection log.
