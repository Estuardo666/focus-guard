# Architecture

```text
Avalonia Desktop
      |
FocusGuard.Client (shared transport and client workflows)
      |
Versioned Named Pipes v2
      |
Windows Service (authoritative state)
   |                 |
Per-user ProcessAgent Native messaging host
   |                 |
Windows process APIs |
                     |
                 Browser extension
```

Domain rules remain platform-independent.

The desktop UI never terminates processes or decides whether a session expired. The service owns policy activation, expiration, and enforcement state.

`FocusGuard.Client` has no Avalonia, Desktop, Service or Win32 dependency. It
contains the existing control client/codecs integration and a `SessionWorkflow`
that resolves a saved local profile, copies its rules into a normal activation
request and passes through authoritative status. It also maps explicitly reviewed
cycle/strict timer snapshots, performs the normal prepare/activate handshake and
forwards prepared activation, normal stop and break skip without retries or a
second preparation after a lost response. The service still owns every
activation, expiration and enforcement decision. Client failures do not imply
that the session ended, and activation is never automatically retried.

`SessionConnectionTracker` now separates read evidence from session state:
unknown before a successful status read, connected after a ready service reply,
and disconnected/stale with the last confirmed snapshot retained on failure.
Non-ready replies do not replace confirmed state; caller cancellation does not
invalidate a healthy connection. Avalonia preserves time/phase/strict/deadline
while stale, shows localized unconfirmed status and disables mutation until a
fresh status reconciles it. A lost mutation response also requires reconciliation.
Only a fresh backend terminal state can close the session in the UI.

Avalonia remains the shipped UI. It uses the shared workflow for normal timers
and status/cycle/strict requests, while retaining strict review, pending-review
presentation, recovery-code display/cancel, presentation and
framework lifetime callbacks. `ProfileWorkflow` now owns reusable profile creation, rename,
duplicate naming/rule IDs/cycle settings and rule edits, using the existing
Storage boundary for profiles and presets. `DesktopSettingsStore` and
`SessionJournalStore` are shared Client data components with unchanged JSON;
`SessionJournalWorkflow` coordinates accepted preferences, intentions, observed
session fallback and reflections. UI commands/events/labels remain in Desktop.
Profiles stay in user-local SQLite;
strict state, schedules and history remain owned by the service. The service
runs as LocalService; process enforcement is performed by the per-user agent,
and web enforcement by the Chrome/Edge extension.

Tests use isolated SQLite and pipes, controlled time and policy consumers;
installed enforcement, reboot and elevated recovery require separate drills.
The progressive UI migration is specified in `FOCUSGUARD_UI_MIGRATION_PLAN.md`.

## Native UI platform boundary (FG-170)

FocusGuard.Client.Platform defines catalog, bounded top-down BGRA icon buffer,
startup registration, agent launcher and desktop-instance ports.
FocusGuard.Platform.Windows implements Start menu COM discovery, shell/GDI
icons, HKCU startup registration, agent launch/supervision and named mutex/event
lifetime. It references Client and has no Avalonia or enforcement dependency.
A separate library avoids introducing Client into the service infrastructure.
Desktop converts pixels to Avalonia Bitmap and dispatches activation to its UI
thread. It owns cancellation; closing it never stops the agent or session.

Pixels are copied and bounded to 256x256, with no native handles. PNG encoding
for the future WebView transport is deferred until its host exists. These ports
are not IPC endpoints or permission to expose arbitrary files to a renderer.

## ProcessAgent listener request lifetime (FG-171)

The existing sequential ProcessAgent endpoint bounds every connected request to
five seconds, using the injected TimeProvider timer and a token linked to host
shutdown. Framing reads, status polling, acknowledgement confirmation, audit
write and response write share that token. Idle/partial peers lose their slot
when it expires; truncated/oversized frames and disconnects are contained to
the connection. Expected cancellation during shutdown completes the listener.
The wait for a new peer uses host cancellation only and does not churn idle
server instances. Pipe names, ACL, protocol version and policy authority remain
unchanged. A pipe-name constructor seam permits isolated real-pipe tests.

Status still precedes policy exchange, so the first heartbeat after a cycle
boundary receives the correct break delivery. Audit authorization remains tied
to the process channel, regardless of unconfirmed browser health. No automatic
replay is introduced. Availability under repeated hostile connections, caller
identity/role checks and the client's retained confirmation on disconnect are
separate open Phase 3B/4 gates.

## Process agent client and local confirmation (FG-172)

Program now composes the unchanged enforcement worker with NamedPipeProcessAgentClient
and ProcessAgentConnection. One actor exchanges bounded heartbeat/audit frames
every 500ms; connect/read/write share a two-second TimeProvider deadline. Endpoint
and mutex follow ProcessAgentProtocol.PipeName for development/production.
The agent opts into a three-second monotonic confirmation lease in its receiver;
other receiver consumers retain their existing behavior. Failed exchange, invalid
proof, EOF, caller shutdown and authority changes clear local delivery/ack state.
No stop or recovery command is sent. Reconnection requires fresh delivery then
exact ID/hash acknowledgement. Listener instance ID is stable for its lifetime;
a changed instance invalidates confirmation even for a restored identical policy.

Agent audits are bounded to 256, bound to the producing session, consumed once
before send and dropped on uncertain exchange. They can be lost, but are never
replayed or attributed to a new session. Supervisor discovery excludes another
Windows session; cancellation of Desktop supervision does not kill the child.
A live cooperative TestTarget verifies restart/survival, without launching a
real enforcement agent or interacting with installed sessions. Supervision still
ends when Desktop closes; HKCU agent startup remains the independent login path.
Phase 4 must add peer/role authorization before a real new UI pilot.

## IPC foundation and dormant WebView host (FG-173/174)

FocusGuard.Ipc.Windows supplies strict owner/logon/role policy, protected deployment
manifest parsing with native exe+managed entrypoint hashes, Identification capture,
SCM server proof, canonical opened paths, and exact local pipe rights/first-instance
primitives. It is not referenced by distributed service/client transports yet;
existing authenticated-user endpoints remain pending integration. Real LocalService
interactive process metadata needs explicit native consent or another OS-backed
identity source. No process-object metadata ACL grant has been implemented.

FocusGuard.Host.Windows produces FocusGuard.UI.exe and local ui assets alongside
Avalonia. Its native composition denies IPC until Phase4 authorization is wired.
The only allowed v1 bridge command is status.get, with origin/document proof, 4KiB
request/8KiB response, one in-flight read, 60/minute bound and a three-second deadline.
It cannot stop/start sessions or run arbitrary commands. Navigation is local entry
only; resources are the three enumerated shell assets; remote/frame/popup/download/
permission/auth/hostobjects/scriptdialogs are rejected. Runtime failures use native
Retry/Close. Closing/recreating renderer invalidates document/cancels reads, sends no
stop, and leaves service/agent authority intact. Current smoke has no service calls.

Package0.1.24 adds the dormant host; existing Desktop shortcut/login launch remain
unchanged. Host install files and embedded hashes are verified; no installation has
been performed. Setup invokes Windows PowerShell with system-only PSModulePath,
preventing elevated auto-import from current-user modules. The readonly native
child probe confirms Windows may add protected ProgramFiles modules, but user
profile paths are absent. Package0.1.23 is superseded before release.

## Consented metadata and control shutdown (FG-175)

Owner explicitly approved QUERY_LIMITED_INFORMATION0x1000 for client identity
verification. CurrentProcessMetadataConsent adds exactly a LocalService allow ACE
on the current client process only, preserves existing denies/ACEs, reference-counts
concurrent scopes and restores original DACL on final disposal. Absent consent
changes nothing. Protected manifest flag defaults false. Authenticated client and
server contexts now connect native deployment proof, owner scope and role checks,
but are not yet referenced by distributed transports/endpoints. SCM own-process
PID/session0/account/config plus protected disk image hashes remove the need for
clients to query the LocalService process object, which was denied in a live readonly
probe. No service-account escalation or Identification downgrade.

Control listener owns its pending stream until connection transfers it to a handler;
shutdown disposes the pending stream before draining connected handlers. This fixes
a reproduced connectable endpoint left behind after StopAsync. Stopping a listener
cancels incomplete reads without stopping the authoritative session. Native transport
DACL denial is now exercised using a restricted token, with normal exchanges before
and after. This is a restricted-SID proof, not an installed second-user/LocalService
compatibility proof. Actual renderer-crash smoke remains unproven: interrupted agent
work archived and validated host restored; no unknown renderer was killed.

## Production native authorization composition (FG-176)

Production clients and service now reference Ipc.Windows through composition; pure
Client uses Contracts factory interfaces only. Unconfigured transport cannot access
installed endpoints. Verified clients validate their protected deployment/owner/role,
grant lifetime-scoped consented query0x1000 only to LocalService, and validate SCM
server before data. Native role checks precede all service side effects. One owner
logon is bound while authenticated client process handles are alive; after all exit,
a fresh logon for the same provisioned owner may bind. Next accept is reserved before
prior stream disposal, preserving first-instance namespace ownership.

Setup explicitly explains metadata consent before changes. Install resolves actual
WTS session user or explicit owner, protects every file/owner and atomically generates
IPC policy; owner-hive registrations and exact SCM config are retained in rollback.
Source+0.1.26 package validated, but current installed version is untouched; real
LocalService/owner compatibility/rollback gate remains pending. Host read-only bridge
can authorize only after native composition succeeds; smoke/copies stay closed.

FG177 adds fixed --smoke-renderer-crash diagnostic only. Native Page.crash targets this isolated smoke WebView, not a JS bridge or external PID. Success requires actual RenderProcessExited plus a fresh recreated document reply; neither synthetic callback nor command return establishes success. Published0.1.27 four smoke paths passed. LocalService installation remains unchanged/pending owner-present proof.

FG178 fixes installed startup failure from unequal final directory separators. Both fully-qualified deployment roots use shared root-preserving canonicalization; parsed manifest and authorization root are canonical before trusted image paths are built. Sibling/relative roots remain rejected, ACL/hash/role protections unchanged. Owner reported0.1.27 failure; it must not be retried. Corrected0.1.28 validated, actual stopped service untouched pending owner-present install.
