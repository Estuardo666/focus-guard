# FG-136 — quiet, optional Windows startup plan

## Acceptance criteria

1. `FocusGuard.ProcessAgent.exe` runs without creating a console window.
2. Windows logon launches the Desktop with an explicit minimized argument and
   the main window remains in the notification area until requested.
3. Settings exposes a localized, accessible switch for enabling or disabling
   Desktop startup at Windows logon.
4. Disabling Desktop startup does not disable the separately registered
   ProcessAgent or change service-owned session expiration.
5. Automated tests cover startup argument parsing, registration behavior,
   settings persistence, hidden initial presentation, and installer contracts.

## Implementation plan

1. Publish ProcessAgent as a Windows GUI subsystem executable while preserving
   its existing background loop and IPC behavior.
2. Add a fixed-name HKCU startup registration boundary for the installed
   Desktop and use an explicit `--minimized` argument.
3. Bind the registration state to a semantic Settings switch and localize its
   label and explanation.
4. Keep normal launches and second-instance activation visible; hide only the
   first window created from the minimized startup argument.
5. Run formatting, build, relevant tests, installer validation, and graphify.

## Implications

- Security: the service remains authoritative, the Desktop still performs no
  enforcement, and the ProcessAgent remains separately available for recovered
  finite sessions.
- UX: logon is quiet, the agent has no closable console, and startup becomes an
  explicit user preference.
- Recovery: disabling UI startup does not remove enforcement recovery; missing
  agents still fail open and remain visibly degraded when the Desktop is opened.
