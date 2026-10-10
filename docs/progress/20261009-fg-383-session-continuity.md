# FG-383 — continuidad de sesión instalada .133

## Resultado
Una sesión normal timer-only iniciada desde el Host React instalado conservó el `SessionId` y `EndUtc` cuando se cerró y reabrió el Host. El temporizador llegó a Expired en el fin original, sin detenerse antes.

## Secuencia y evidencia
- Preflight: FocusGuard Service Running como LocalService; Host .133 `--pilot` del usuario medio, Service/Extension Ready, sin sesión. Perfil `2`; Strict OFF.
- React revisó y confirmó Timer `1 min`, enforcement OFF. La revisión dice «Sólo temporizador. No se aplicarán restricciones».
- Diagnostic Active previo: SessionId `d8a8a35e-89dd-4f9f-9cbe-2f497fc9911e`, EndUtc `2026-10-09T20:38:05.9596933Z`, enforcementConnected=false (bloqueo no solicitado).
- `CloseMainWindow` al Host PID33684 devolvió true y el proceso salió. Diagnostic después del cierre siguió Active con el mismo SessionId/EndUtc y 24s restantes; no se envió Stop.
- Reabrí como propietario con `FocusGuard.UI.exe --pilot`, PID668. Diagnostic devolvió Active, el mismo SessionId/EndUtc y 17s restantes. Después expiró naturalmente. Diagnostic final: Expired, mismo ID/fin, ServiceReady/ExtensionReady true; el Host muestra «Sesión completada · 1 min · 2».
- `desktop-settings.json` cambió al confirmar la sesión; restauré la copia original byte por byte: SHA `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55`, longitud288, LastWrite `2026-10-09T18:19:04.0745349Z`. La actividad completada permanece en historial; no la eliminé.
- Estado final: Host PID33288, `--pilot`, Resumen, 1216×799; Service LocalService Running, sesión Expired, Service/Extension Ready.

## Observación visual para FG-384
La captura del Host activo antes del cierre muestra el borde superior del encabezado cortado. Tras enfocar duración en el formulario, el scroll no se reinicia al llegar un `SessionId` nuevo; el efecto actual solo resetea al montar y al terminar la sesión. Evidencia de la captura activa y estados Diagnostic en `D:\TEMP\FocusGuard-FG383-session-continuity-20261009\`.

## Próximo gate
Corregir el scroll al iniciar una sesión nueva y comprobar el encabezado activo; después repetir el flujo con enforcement real para verificar continuidad/reconexión del Agent. Esta prueba no demuestra bloqueo de procesos/web ni ACK del Agent porque enforcement estuvo apagado.