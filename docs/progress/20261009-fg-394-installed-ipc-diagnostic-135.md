# FG-394 — par IPC permitido/denegado en .135

## Resultado (2026-10-09)

Desde `C:\Program Files\FocusGuard\FocusGuard.DrillClient.exe`, compilado con rol `Diagnostic`, `status` fue aceptado por el servicio instalado y devolvió `serviceReady=true`, `extensionReady=true`, `SessionState=Expired` y `SessionId=38733498-178d-46d7-ad71-e3f0d3d17cce`. El mismo cliente intentó `probe-agent-role-denied` contra el pipe de ProcessAgent y recibió `DENIED_EOF` (salida 0, resultado esperado). Una segunda consulta `status` confirmó el mismo estado y SessionId.

`FocusGuard` permaneció Running como `NT AUTHORITY\LocalService`, PID 35760. No se inició ni detuvo ninguna sesión. Esto cierra un par instalado de autorización para Diagnostic; no demuestra la matriz completa de todos los roles/clientes ni el rollback del instalador.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg394-installed-ipc-diagnostic-allow-deny-135.json`.
