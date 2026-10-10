# FG-365 — Native Messaging instalado: status permitido y origen inválido

## Objetivo

Validar que el NativeMessaging .123 instalado, autenticado desde Program Files como el rol NativeMessaging, consulta status al servicio LocalService con el origen registrado y rechaza un origen no permitido.

## Plan de prueba

1. Leer el status Diagnostic y guardar SessionId/SessionState/Strict/Enforcement antes.
2. Leer el origen permitido de C:\Program Files\FocusGuard\focusguard.native.json.
3. Lanzar el NativeMessaging instalado con ese origen y una única consulta protocolVersion 1, type=status, correlationId y sentAtUtc; los campos no usados van null. Comprobar healthStatus, correlación, ServiceReady y ExtensionReady.
4. Lanzar el mismo binario con un origen inválido y sin stdin; comprobar exit 3, antes de conectar al servicio.
5. Volver a consultar Diagnostic y confirmar que el estado de sesión no cambió.

## Seguridad y alcance

No se abre Chrome/Edge ni se envían heartbeats, ACKs de política, URLs, auditorías o capturas. Se trata de una consulta de salud de solo lectura. El origen permitido se copia del manifiesto instalado, no se inventa.

## Resultado

El ejecutable instalado `C:\Program Files\FocusGuard\FocusGuard.NativeMessaging.exe` aceptó el origen copiado de `focusguard.native.json` y devolvió un único `healthStatus` con la correlación enviada, `serviceReady=true`, `extensionReady=true`, `SessionState=Expired` y sin error. El proceso terminó con exit 0. Con `chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/` y stdin cerrado, el host terminó con exit 3 antes de IPC. Diagnostic status antes y después permaneció estable: `Expired`, mismo SessionId `ed3f69ed-5e08-4264-b65a-3c0542d19e31`, Strict=false y `enforcementConnected=false`.

No se abrió Chrome/Edge ni se envió heartbeat, ACK de política, URL, auditoría o captura. Evidencia: `artifacts/fg365-output/installed-native-messaging-status-and-origin.json`.
