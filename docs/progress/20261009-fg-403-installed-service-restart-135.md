# FG-403 — reinicio del servicio instalado .135

## Resultado (2026-10-09)

Con el servicio Running como LocalService PID 35760 y sesión Expired/Strict OFF, usé el helper elevado de una sola vez (ya autorizado) para ejecutar el reinicio. El service PID cambió a 5892 y volvió a Running como LocalService. `FocusGuard.DrillClient.exe status` salió 0 y devolvió `serviceReady=true`, `extensionReady=true`, `sessionState=Inactive`, sin SessionId. El Host instalado PID 35304 permaneció abierto; al volver a consultar la UI, Actividad mostraba el estado actualizado.

No había sesión activa, no se instalaron paquetes ni se modificaron perfiles. `desktop-settings.json` conserva el SHA original `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55` y LastWrite 13:19.

Esto valida un reinicio del servicio sin sesión activa y la recuperación de sus consultas autenticadas. No demuestra rollback del instalador ni continuidad de una sesión durante el reinicio del propio servicio.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg403-installed-service-restart-135.json` y `fg403-service-restart-elevated-result.json`.
