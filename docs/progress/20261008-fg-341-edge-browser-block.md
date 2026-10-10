# FG-341 — Bloqueo web en Edge con el servicio instalado

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 7, aceptación instalada  
**Estado:** bloqueo real confirmado en Edge; Host y servicio .112. La extensión preexistente de Edge es .98.

## Plan de verificación

1. Confirmar el servicio LocalService, Host React .112 y estado sin sesión.
2. Verificar que Edge tiene el Native Messaging Host registrado y una extensión FocusGuard habilitada con el origen permitido.
3. Iniciar desde React una sesión normal finita con reglas web Block y Strict apagado.
4. Navegar desde una pestaña Edge nueva a una URL de prueba bloqueada y comprobar Activity y la pantalla de descanso.
5. Cerrar la pestaña de prueba y detener la sesión normal desde la confirmación de React.

## Resultado

Edge tenía FocusGuard **0.1.98** como extensión unpacked habilitada. Su ID (`emhdinkjpinjnmldipnejnbeefgonjnj`) coincide con el `allowed_origins` del Native Messaging Host registrado. Edge PID23888 lanzó `FocusGuard.NativeMessaging.exe` PID33256 durante la prueba.

La UI React instalada .112 inició una sesión normal de dos minutos desde el perfil `2`, con nueve sitios Block, enforcement activo y Strict apagado. Navegar a `https://facebook.com/focusguard-edge-fg341` llevó a la página local de FocusGuard. El servicio registró el evento `80cc54ce-e8e3-4ad8-90b4-6d12199edcf1`, `kind=1`, `outcome=Blocked`, bajo SessionId `75e20e44-a8e7-4851-b50f-4f5c4b7cfa42`. La UI mostraba «facebook.com will still be there later».

La pestaña de prueba se cerró; Edge quedó con la pestaña previa de `edge://extensions`. React mostró «¿Detener esta sesión normal?» y, al confirmar, el servicio pasó a `Stopped`, con Service/Extension listos, Strict apagado y EnforcementConnected=false.

## Evidencia

- `artifacts/fg341-edge-browser-acceptance.json`: preflight React, identidad de la extensión, SessionId, Active/EnforcementConnected y evento Activity `Blocked`.
- `artifacts/fg341-edge-cleanup.json`: cierre de la pestaña de prueba y parada normal confirmada.

## Seguridad, UX y límites

Se usó la extensión ya instalada en Edge; no se habilitó Developer Mode, no se cargó otra copia ni se cambiaron sus ajustes. La sesión era normal y finita; no se inició sesión en Facebook ni se cargó su contenido. La pestaña temporal se retiró y el servicio quedó sin enforcement.

La extensión Edge instalada es .98 mientras Host/Service son .112. Esta prueba demuestra que la versión presente bloquea el destino con el servicio .112; no demuestra que el paquete Edge .112/.116 esté instalado ni valida sus cambios recientes de ACK. El cierre de versión y la repetición sobre .116 siguen pendientes.

## Siguiente paso

Actualizar Edge a la versión del paquete FocusGuard vigente y repetir el bloqueo con .116 una vez instalada esa versión. Mantener abiertos el handshake Agent independiente bajo política activa, reinicio/rollback instalado, paridad de funciones y Fases 8–10.
