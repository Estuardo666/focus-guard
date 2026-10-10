# FG-404 — reinicio del servicio instalado .135 con sesión activa

## Plan

Con autorización explícita del propietario (chat, 2026-10-09) y un contexto ya elevado, iniciar desde el Host React una sesión normal (`gpt test`, Temporizador, enforcement ON, Strict OFF), reiniciar el servicio `FocusGuard` con `Restart-Service -Force`, sondear `FocusGuard.DrillClient.exe status` y comprobar si se conservan SessionId/EndUtc, si el Agent reconecta y si `FocusGuard.TestTarget.exe` sigue bloqueado hasta la expiración natural. Sin UAC automatizado ni cambios de cuentas/ACL.

## Resultado (2026-10-09)

**La sesión no se conserva.** Esto coincide con el comportamiento documentado (`SPEC.md:214`, `THREAT_MODEL.md:101` y la fila «Persistencia estricta» del plan: las sesiones normales no se restauran tras reiniciar el servicio o el equipo), así que no es una regresión; FG-404 lo caracteriza en la instalación real.

- 23:58:24 sesión `20d56ccb…` Active con `enforcementConnected=true`; EndUtc fijado en la revisión (00:00:30).
- 23:58:35 reinicio: servicio PID 5892 → 11288, Running como LocalService; primer `status` correcto a las 23:58:37.
- Después del reinicio: `Inactive`, sin SessionId, `enforcementConnected=false` durante unos 75 s de sondeo; nunca se recuperó.
- Un TestTarget lanzado durante el reinicio (PID 22592) y otro lanzado después (PID 36388) **no fueron terminados**; los detuve manualmente.
- El Host (PID 35304) siguió abierto y muestra «Nueva sesión / Sin sesión activa» **sin aviso de interrupción**.
- La sesión perdida **no aparece en `history.db` ni en `audit/events.jsonl`**.
- Agent PID 28088 sin cambios.

Limitación de instrumentación: el script no detectó el TestTarget lanzado antes del reinicio, así que no se reclama un bloqueo previo en esta corrida. El enforcement previo solo consta por `enforcementConnected=true`; FG-391/FG-402 ya cubren el bloqueo con el servicio estable.

## Restauración

`profiles.db` sin cambios (SHA `01CE3E83…`, 61,440 bytes). El Host reescribió `desktop-settings.json` al iniciar (copia guardada como `fg404-desktop-settings-after-test-before-restore.json`); lo restauré al SHA `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55` con LastWrite 13:19. Estado final: ServiceReady/ExtensionReady, Inactive, Strict OFF, sin TestTarget en ejecución, ventana del Host minimizada como estaba.

## Implicaciones

- **Seguridad/recuperación:** reiniciar el servicio con privilegios de administrador termina una sesión normal sin dejar rastro. Es coherente con «Strict debe ser difícil de desactivar casualmente, no imposible», pero la ausencia de un registro en audit/historial reduce la trazabilidad.
- **UX:** el usuario pierde la sesión sin explicación. Hace falta una decisión del propietario: (a) mantener «normal no persiste» y añadir un aviso de interrupción, o (b) persistir sesiones normales.
- Los instaladores que reinician el servicio (ver la sección de actualizaciones del plan) pierden igualmente la sesión normal activa; esto ya está documentado como HIGH RISK.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg404-installed-service-restart-active-session-135.json` y `fg404-service-restart-active-session-raw.json` (cronología completa).

## Siguiente tarea

Decisión del propietario sobre la continuidad de sesiones normales tras reiniciar el servicio y sobre el registro de la interrupción; mientras tanto, siguen pendientes de Fase 3/9 la prueba equivalente con Strict (que sí persiste por snapshot) y reboot/reloj.
