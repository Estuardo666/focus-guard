# FG-150 service status stall plan

## Symptom and initial root-cause hypothesis

After a verified standalone reinstall, Desktop persists `enforcementEnabled: true`
but an active session reports `Timer only` and neither application nor website
blocking works. The production control and extension pipes exist but do not accept
new requests; the service remains running while its thread and handle counts grow.

Every Desktop, ProcessAgent, and extension heartbeat calls
`InMemoryFocusService.GetStatusAsync`. That method currently reloads and decrypts
strict persistence on every call, including inactive and normal sessions. Concurrent
callers can therefore congest all three listeners on one unrelated recovery path.

## Investigation correction

The first live connectivity probe was compiled against the development IPC names,
so its timeout did not prove that the production service was stalled. After the
drill client was corrected to the production control pipe, the installed service
answered and reported both runtime components ready. The repeated strict-state
load remained a reproduced service defect (25 loads for 25 concurrent status
queries), but it was not treated as proof of the screenshot's cause.

The screenshot's `Timer only` label was instead reproduced as a Desktop state
notification defect: the first connected active status changed both session and
enforcement state, but did not notify the enforcement presentation properties.
Separately, a cross-runtime integrity fixture exposed a real browser-enforcement
defect for cycle-count sessions: the service hashed a null `totalDurationSeconds`
property while the serialized policy and extension hashed `totalDuration`.

## Plan and acceptance criteria

1. Add a regression test proving strict persistence is loaded once per service
   lifetime even when status is queried repeatedly and concurrently.
2. Make strict-session recovery a one-time, concurrency-safe operation while
   preserving restart recovery and finite fail-open behavior.
3. Update the production drill client to use the current versioned control pipe.
4. Add an installer health verification that rejects a service which is merely
   `Running` but cannot answer the authoritative control pipe.
5. Run formatting, focused and full tests, build, installer contracts, regenerate
   the standalone EXE, and verify its embedded payload.

## Security, UX, and recovery implications

- Security: the service remains authoritative; no enforcement is inferred from a
  timer or process existence. Strict recovery still runs once after every service
  restart and corrupt state still fails open.
- UX: setup must not report success unless the installed service answers IPC.
  Active UI can then show protected or degraded health instead of a stale timer.
- Recovery: no account, ACL, service registration, recovery delay, or expiration
  rule is weakened. The installer retains its existing rollback path.
