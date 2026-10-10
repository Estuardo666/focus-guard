# FG-386 — intención explícita de sesiones programadas

## Problema observado
FG-379 comprobó que un horario timer-only de 1 min se inicia y termina en su hora original, pero el Host mostró “Confirmación de bloqueo: Desconocido”. Los horarios guardan explícitamente `DurationMinutes` y `EnforcementEnabled`; el servicio los usaba para iniciar la sesión, pero no los guardaba junto al resultado histórico. El diario local solo conocía las sesiones confirmadas manualmente.

## Cambio
- El servicio adjunta `ScheduledSessionIntent` (perfil original, duración configurada y EnforcementEnabled) al status de la sesión programada y a su registro de historial.
- SQLite migra `ScheduledIntentJson` de forma aditiva; las sesiones antiguas siguen con metadata nula y no se inventa su consentimiento.
- El Host React reconstruye y guarda el diario local cuando falta o tiene consentimiento desconocido, con `SaveObserved` que no reemplaza una intención manual ya aceptada ni sus notas.
- Avalonia también registra la intención programada cuando observa el inicio.

## Aceptación
1. Pruebas de servicio cubren horarios de 1 min con enforcement desactivado y activado; la duración de repetición es la configurada, aunque el inicio llegue tarde.
2. Al borrar el horario después del inicio, el historial sigue identificando perfil, duración e intención de bloqueo.
3. Las pruebas Host cubren recuperación desde status activo y desde historial después de terminar el horario.
4. La suite de solución pasa; se genera/valida Setup .135 y se instala.
5. En el Host instalado, un horario temporal timer-only de 1 min muestra “No solicitado”, la repetición conserva 1 min y el horario se elimina sin perder el historial. No se lanza una aplicación bloqueada ni se activa enforcement real.

## Evidencia
La prueba instalada quedó documentada abajo; capturas, preflight, logs y JSON están bajo `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\`.
## Resultado instalado (2026-10-09)
- Setup .135 instalado con exit0; health check autenticado pasó con Service como LocalService. Manifiesto31/31; una auditoría posterior vio archivos .129 mezclados en Program Files. Repetí la instalación .135 con exit0; la verificación post-reparación confirmó nueve archivos críticos de Service/Desktop/Host/UI/browser extension por SHA-256. La causa del estado mixto anterior no quedó demostrada. La extensión instalada reporta .135.
- Artefactos .135: Setup SHA-256 `BF10470741DD0FE524E3A5D1A595889D9476F7A376FB91CAF1F04EC19BA88BA6`, ZIP SHA-256 `04A958E4D671AAD68C54DAD28F99D092EC1A556167BC8E709C99DB07BBE53F74`. Pasaron .NET895/895 serial, React145/145, extension19/19, `test-installer.ps1`, Browser Chrome/Edge package tests y Setup `--package-self-test` exit0.
- El Host instalado ejecutó `FG386 1 min timer only` para perfil `2`, viernes 16:45, `SA Pacific Standard Time`, bloqueo desactivado. El servicio inició a `2026-10-09T21:45:03.8328678Z` y conservó EndUtc `2026-10-09T21:46:00Z`; la sesión expiró naturalmente. El resumen de canales quedó Ready/Ready, `enforcementConnected=false` por decisión explícita y la interfaz mostró «No solicitado».
- Eliminé solo ese horario temporal; Programación volvió a «Aún no tienes horarios semanales». El resumen de Activity sigue disponible tras borrarlo. `session-journal.json` conserva SessionId `6e200394-0fab-45bd-8711-bf3145ebaa8b`, perfil `2`, 1 min y `EnforcementEnabled=false`; «Revisar y repetir» prepara exactamente el mismo perfil/duración con bloqueo apagado. Durante FG386 no se lanzó ningún proceso bloqueado ni se visitó un sitio de prueba.
- Los cinco perfiles, cinco reglas de aplicación y treinta sitios, incluidos sus `UpdatedUtc`, siguen iguales. `desktop-settings.json` conserva el SHA-256 original `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55`. La entrada de prueba queda en Activity. Estado final: sin sesión activa, Service/Extension Ready.
- Capturas, preflight, logs de instalación y JSON: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\`.