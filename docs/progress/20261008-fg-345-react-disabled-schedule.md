# FG-345 — Crear y retirar un horario deshabilitado desde React

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 8.5, programaciones semanales  
**Estado:** CRUD de un horario de prueba aceptado en Host/Service .112; no quedó ningún horario de prueba guardado.

## Plan de verificación

1. Confirmar Host React .112 en piloto, service listo, sesión detenida y Strict apagado.
2. Abrir una programación nueva y verificar antes de guardar que Auto-start y bloqueo estén apagados.
3. Revisar y guardar una programación con nombre único, perfil `2`, 50 min y el horario local predeterminado; conservarla deshabilitada.
4. Reabrir el registro para confirmar que sigue deshabilitado, timer-only y con el perfil/duración elegidos.
5. Eliminar sólo el registro temporal desde React y verificar la lista vacía y que el servicio siga detenido.

## Resultado

El servicio instalado estaba `Stopped`, `ServiceReady=true`, `ExtensionReady=true`, Strict apagado y sin horarios guardados. React abrió «Nueva programación» con perfil `2`, duración 50 min, bloqueo `Off`, auto-start `Off` y días L–V seleccionados. La revisión mostró el horario como «Pausada».

Se guardó `FG345-ScheduleProbe-3cf8ccf4`; al reabrirlo, React confirmó perfil `2`, duración 50 min, bloqueo `Off` y auto-start `Off`. No se modificó, habilitó ni ejecutó ningún horario existente.

El registro se eliminó desde la confirmación «Eliminar horario · FG345-ScheduleProbe-3cf8ccf4». La UI volvió a «Todavía no hay sesiones programadas». El servicio siguió en `Stopped`; Service/Extension siguieron listos, Strict apagado y EnforcementConnected=false.

## Seguridad y límites

El horario temporal estaba explícitamente pausado, con timer-only y auto-start deshabilitado; no podía iniciar una sesión ni aplicar bloqueos. La prueba modificó y eliminó sólo el registro nuevo mediante React/LocalService; no escribió SQLite ni el JSON de ProgramData directamente. Esta aceptación cubre una sola alta/lectura/baja deshabilitada; no prueba ocurrencia real, auto-start, solapes, DST, recuperación tras reinicio ni cambios de reloj.

## Evidencia

- `artifacts/fg345-output/fg345-schedule-review.json`: preflight y revisión pausada.
- `artifacts/fg345-output/fg345-schedule-created.json`: fila visible después de guardar.
- `artifacts/fg345-output/fg345-schedule-readback.json`: valores al reabrir sin guardar cambios.
- `artifacts/fg345-output/fg345-schedule-cleanup.json`: confirmación de baja, lista vacía y service listo.
