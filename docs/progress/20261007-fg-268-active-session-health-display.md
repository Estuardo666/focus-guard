# FG-268 — Countdown y salud de canales durante una sesión

## Verificación instalada

Después de FG267 se inició una segunda sesión normal de1min en el Host .91 para capturar estado activo. UIA preconfirmó Strict `Off`, bloqueo `Off`, timer seleccionado y duración1. La revisión volvió a mostrar que era sólo temporizador, sin restricciones. No se modificaron perfiles ni reglas.

La captura WebView2 a1216×799 muestra «Sesión en curso», `00:00:15`, el fin exacto y el modo normal. En la misma toma, DrillClient devolvió `Active`, remaining14, `serviceReady=true`, `extensionReady=true` y `enforcementConnected=false`. La vista UIA del renderer expone las etiquetas `Conectado` para Service y `Disponible` para extensión. Estas tarjetas de salud están más abajo en el documento y requieren scroll en la ventana actual; el primer viewport da prioridad al timer.

La SessionId `474b3554-ae14-481d-9117-fe5daae61dd1` expiró automáticamente a `Expired`/0, sin enviar `Stop`. Evidencia: `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\installed-host-0.1.91-session-active.png`, `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\installed-host-session-active-uia.json` y `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\installed-normal-session-continuity-0.1.91.json`.

## Límites

Confirma que countdown y salud de canales se publican en la UI/árbol accesible durante Active. No valida bloqueo de aplicaciones, navegación bloqueada en Edge ni `enforcementConnected=true`; `extensionReady` en esta máquina proviene de Native Messaging bajo Chrome. Las tarjetas de salud no están en el primer viewport de1216×799.
