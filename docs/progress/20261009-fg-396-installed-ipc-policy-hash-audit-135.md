# FG-396 — auditoría del manifiesto IPC instalado .135

## Resultado (2026-10-09)

Leí `C:\Program Files\FocusGuard\ipc-policy.json` sin modificarlo. `schemaVersion=1`, `installRoot` coincide con Program Files, el SID owner corresponde al usuario actual, la ACL está protegida y el propietario del archivo es Administradores. El servicio está Running como LocalService (PID 35760).

Los SHA-256 de EXE y DLL coinciden con la política para seis entradas: Service, Desktop, ProcessAgent, NativeMessaging, WebViewHost y Diagnostic. El hash del manifiesto es `B86740DB14DD1A16A3DCA1F534F901794A825E176D95009DD0A52221B9B0F766`.

Junto con FG394, esto demuestra el status autorizado de Diagnostic y su rechazo al pipe ProcessAgent (`DENIED_EOF`). No demuestra la matriz completa de todos los clientes/roles ni la autenticidad desde cada cliente, y no sustituye la prueba de rollback del instalador.

Evidencia detallada por archivo: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg396-installed-ipc-policy-hash-audit-135.json`.
