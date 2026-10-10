# FG-406 — aceptación instalada .136 del registro de interrupción

## Plan

Con autorización explícita del propietario (chat, 2026-10-10), instalar 0.1.136 sobre .135 y repetir FG-404 en el equipo real, comprobando lo que añadió FG-405: historial, audit, liberación del bloqueo y aviso del Host.

## Instalación

`install-focusguard.ps1 -PackageDirectory artifacts\installer\FocusGuard-0.1.136 -ClientMetadataQueryConsent` (contexto ya elevado) terminó con exit 0, health IPC autoritativo correcto y servicio Running/Auto como LocalService. `FocusGuard.Service.dll`, `FocusGuard.Contracts.dll`, `FocusGuard.UI.exe`, `ui\host.js` y `FocusGuard.ProcessAgent.exe` instalados coinciden por hash con el payload. El instalador cerró el Host; lo reabrí con `--pilot` mediante un acceso directo, a integridad media.

## Resultado (2026-10-10)

- 00:23 sesión `616cea7f…` (`gpt test`, Temporizador 3 min, enforcement ON, Strict OFF) Active con `enforcementConnected=true`. Un TestTarget lanzado antes del reinicio fue terminado (audit `Terminated` 05:23:14.82Z).
- 00:23:15.38 `Restart-Service FocusGuard -Force`: servicio PID 38236 → 34096, LocalService.
- **Audit:** `SessionInterrupted` (kind 4) a las 05:23:15.40Z con el mismo SessionId, destino «FocusGuard service», resultado «Stopped».
- **Historial:** `history.db` escrito a las 00:23:15.40; Actividad muestra «gpt test · Menos de 1 min · Sesión interrumpida».
- **Bloqueo liberado:** tras el reinicio el estado es Inactive, sin SessionId; un TestTarget nuevo ya no se bloquea (detenido manualmente).
- **Host:** el aviso `role="alert"` «La sesión se interrumpió» aparece unos 6 s después del reinicio; «Ver actividad» lleva a Actividad y lo cierra.
- `service-failures.jsonl` sin entradas nuevas.

Nota de instrumentación: el filtro del script para el `Terminated` previo dio false por cómo se interpretaron las fechas; el evento está en `DrillClient activity`.

## Restauración

`profiles.db` sin cambios (`01CE3E83…`). `desktop-settings.json` restaurado al SHA `987E6001…2D55` con LastWrite 13:19 (copia de la versión del test en `fg406-desktop-settings-after-test-before-restore.json`). Estado final: Ready/Ready, Inactive, Strict OFF, sin TestTarget. El Host queda abierto con `--pilot` y minimizado.

## Implicaciones

- **UX:** la pérdida deja de ser silenciosa en la instalación real.
- **Seguridad:** el reinicio por un administrador queda trazado en audit e historial; no cambia la capacidad de terminar una sesión normal.
- **Limitación:** un crash del servicio sigue sin registro (no probado aquí, por diseño).

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg406-installed-interruption-record-136.json`, `fg406-service-restart-interruption-raw.json` e `install-0.1.136-elevated.log`.

## Siguiente tarea

Pendientes de Fase 3/9: reboot/recovery/reloj, rollback real contra baseline, Edge real, DPI y accesibilidad en Windows 11.
