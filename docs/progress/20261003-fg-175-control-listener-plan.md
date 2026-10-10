# FG-175 — Phase4 listener resource ownership

Prior goal turn made progress: native library/host/package0.1.24 verified and synced; full goal and metadata consent question remain active. No authorization inferred from silence.

Hypothesis: FocusControlPipeService does not dispose the pending NamedPipeServerStream if WaitForConnectionAsync is canceled, leaving the namespace/connectable instance alive after BackgroundService.StopAsync. Reproduce with isolated GUID pipe and real status request followed by Stop. Track pending-stream ownership explicitly, transfer only connected streams to handlers, dispose pending stream in finally, retain16 slots and existing5-second request deadline. No session/policy semantics/ACL/identity changes. Add bounded stop/read/namespace reuse proof, run full suites and regenerate package0.1.25 if distributed code changes.

Independent Luna xhigh subagent adds actual restricted-token access denial to isolated native transport test only; no account/process object DACL changes or installed endpoint calls. A restricted token is not proof of a real second-user/LocalService deployment, and those gates remain pending. Native renderer actual crash and metadata protocol consent still unresolved.
