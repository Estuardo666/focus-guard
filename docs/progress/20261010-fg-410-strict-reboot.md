# FG-410 (parcial) — reinicio real de Windows con sesión estricta activa (.138)

## Preparación

Desde el Host (modo piloto) inicié una sesión **estricta** `gpt test`: temporizador de 10 min, enforcement ON, SessionId `d27cd82e-f304-4065-9f75-943b2e0d74f9`, fin a las 01:51:13 local. La revisión estricta mostró el código de recuperación; se confirmó «Ya guardé el código» **sin copiarlo ni almacenarlo**, porque la sesión expira antes de la espera mínima de recuperación de 15 min. Snapshot `active-policy.json` presente. Armé el drill con `arm-fg112-production-reboot-drill.ps1 -Arm -OwnerPresent` (RunId `0a3fec06-4a82-4dde-a1fd-1b2bbf83aec6`). El propietario reinició y volvió a abrir Claude elevado.

## Resultado — rama «aún activa» (2026-10-10)

`complete-fg112-production-reboot-drill.ps1 -RunId 0a3fec06… -OwnerPresent` (01:45, unos 2 min después del arranque de las 01:43:11):

- `restartObserved=true`.
- Mismo SessionId recuperado por el servicio de producción.
- `Active`, `strictMode=true`, `enforcementConnected=true` antes y después de esperar los canales reales: Desktop/ProcessAgent arrancaron solos a las 01:44.
- Un TestTarget lanzado tras el reinicio fue **terminado** (audit 06:45:29Z con el mismo SessionId).

## Liberación natural

A las 01:51:20 el estado era `Expired`, `enforcementConnected=false` y sin `active-policy.json`. Un TestTarget lanzado después siguió en ejecución (detenido manualmente): el bloqueo quedó realmente liberado.

## Defecto del drill corregido

La rama «ya expirada» de `complete-fg112-production-reboot-drill.ps1` exigía `status.strictMode=false`. El servicio, en cambio, usa `StrictMode` como **tipo** de la sesión y lo mantiene en `true` cuando una sesión estricta expira, y varios consumidores dependen de ello (bridge del Host, workflows, validación IPC). Si el propietario hubiera reiniciado después de las 01:51, el drill habría fallado aunque la liberación fuera correcta.

Ahora la liberación se prueba con `enforcementConnected=false` y la ausencia del snapshot, con un comentario que lo explica. `test-fg112-reboot-drill.ps1` y `test-installer.ps1` pasan. Volví a ejecutar el drill ya expirado: la rama de expiración pasa (`stateBeforeRealChannels=Expired`, `enforcement=false`, `strictMode=true`).

## Restauración

`desktop-settings.json` restaurado al SHA `987E6001…`; `profiles.db` sin cambios; sin TestTarget; `service-failures` sin entradas nuevas; servicio Auto/LocalService.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg410-strict-reboot-armed.json`, `fg410-strict-reboot-result.json` (rama activa) y `fg410-strict-reboot-expired-branch-result.json`.

## Pendiente

Cambios de reloj reales, Edge real, DPI 125/150 %, Narrator/teclado/contraste y Windows 11.
