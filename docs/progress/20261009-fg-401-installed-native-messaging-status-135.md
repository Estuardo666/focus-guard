# FG-401 — Native Messaging instalado en .135

## Resultado (2026-10-09)

Invoqué el `FocusGuard.NativeMessaging.exe` instalado desde Program Files con el origin permitido por el manifiesto y envié un único mensaje native-host `status` de solo lectura, con framing de 4 bytes y JSON. La respuesta fue `healthStatus`, exit 0, correlationId correcto, `serviceReady=true`, `extensionReady=true`, `SessionState=Expired`, `EnforcementConfirmed=false` y sin error. Diagnostic status confirmó el mismo SessionId Expired antes y después.

Esto prueba que el binario instalado con rol NativeMessaging puede autenticarse y consultar ExtensionHealth en el servicio LocalService. La invocación fue manual con el origin autorizado; no demuestra que Chrome/Edge haya iniciado el host ni que la navegación web quede bloqueada.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg401-installed-native-messaging-status-135.json`.
