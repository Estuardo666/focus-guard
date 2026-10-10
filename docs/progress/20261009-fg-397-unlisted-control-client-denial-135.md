# FG-397 — denegación IPC de cliente no listado en .135

## Resultado (2026-10-09)

Desde el PowerShell actual (`C:\Program Files\PowerShell\7\pwsh.exe`), fuera de `C:\Program Files\FocusGuard`, serialicé un sobre `StatusRequest` válido con el `FocusGuard.Contracts.dll` instalado y lo envié directamente por `FocusGuard.Control.v2` usando `NamedPipeClientStream`, sin el cliente autenticado. El servicio cerró el canal con `DENIED_EOF`. `FocusGuard.DrillClient.exe status` antes y después confirmó `ServiceReady=true`, `ExtensionReady=true`, `SessionState=Expired` y el mismo SessionId `a94f44fa-3e81-493e-af6f-35cd168b22fd`. Servicio Running como LocalService PID 35760.

Es una denegación real del servicio para un proceso no listado, contrastada con el status permitido del Diagnostic instalado (FG394). El request fue de lectura y no cambió sesión ni configuración. Esto aún no es la matriz completa de los roles Desktop/ProcessAgent/NativeMessaging/WebViewHost ni el rollback.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg397-unlisted-control-client-denial-135.json`.

Nota de ejecución: `NamedPipeClientStream` no admite configurar `ReadTimeout`; la misma llamada leyó EOF (`DENIED_EOF`) inmediatamente, no fue un timeout. La consulta posterior de Diagnostic confirmó la sesión sin cambios.
