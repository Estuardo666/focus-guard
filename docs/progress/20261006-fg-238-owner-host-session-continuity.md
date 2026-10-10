# FG-238 — Timer normal y continuidad del Host .79

## Condición previa

Host/Service/Desktop 0.1.79 están instalados; sus hashes corresponden al paquete FG237. `FocusGuard.DrillClient.exe status` verificó antes de la prueba `serviceReady=true`, `sessionState=Inactive`, `sessionId=null`, `extensionReady=true`. El servicio corre como LocalService, PID44056.

## Prueba instalada

1. Se abrió `FocusGuard.UI.exe --pilot` como el usuario propietario.
2. En el formulario se mantuvo el perfil `2`, Temporizador, duración de un minuto, modo estricto desactivado y `Activar bloqueo durante la sesión` desmarcado.
3. `Revisar sesión` abrió una revisión del servicio con duración60s, hora final exacta y el texto `Sólo temporizador. No se aplicarán restricciones.` La acción de preparar no activó la sesión.
4. Se confirmó explícitamente desde la revisión del Host. La captura `host-0.1.79-active-one-minute.png` muestra `Sesión en curso`, cuenta atrás y servicio/extensión conectados.
5. `FocusGuard.DrillClient.exe status` informó `Active`, 15s restantes, `strictMode=false`, `enforcementConnected=false`, `sessionId=d30dd4a4-a4de-4aa8-91bf-d383020036d9`.
6. Se cerró la ventana normalmente, sin enviar Stop, y se reabrió `FocusGuard.UI.exe --pilot`. El siguiente status informó el **mismo SessionId**, mismas horas UTC de comienzo/fin y 5s restantes.
7. Se dejó expirar el timer. El status posterior informó `Expired`, `remainingSeconds=0`, el mismo SessionId, servicio y extensión listos, y sin enforcement conectado.

## Resultado

Aceptación instalada de Fase7 para lectura de estado, timer normal finito, review/confirm, reloj/fin del servicio y continuidad same-SessionId tras cierre/reapertura. No se habilitó bloqueo, strict, ni se emitió un comando de parada. FocusGuard generó una sesión terminada en Activity para esta prueba. No se afirma aún bloqueo real de aplicaciones/web con Host .79, ciclos, strict, o equivalencia de todas las pantallas.

## Evidencia

- Resumen piloto .79: `D:\TEMP\focusguard-fg166-validation\artifacts\fg237\host-0.1.79-pilot-summary.png`.
- Revisión de 1 minuto: `D:\TEMP\focusguard-fg166-validation\artifacts\fg237\host-0.1.79-review-one-minute.png`.
- Sesión activa antes de cerrar: `D:\TEMP\focusguard-fg166-validation\artifacts\fg237\host-0.1.79-active-one-minute.png`.
- Consulta autorizada del estado: `FocusGuard.DrillClient.exe status` antes de la prueba, después de cerrar/reabrir y después de expirar.
