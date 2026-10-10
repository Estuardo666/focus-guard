# FG-410 (parcial) — reinicio real de Windows con sesión normal activa (.138)

## Preparación

Antes de reiniciar dejé en marcha una sesión normal `gpt test` (temporizador de 30 min, enforcement ON, Strict OFF, SessionId `f45bf12f-b6b8-4b22-be10-cbc8a92e4f78`), con el journal presente. La armé con `arm-normal-session-reboot-drill.ps1 -Arm -OwnerPresent` (RunId `05c03a8e-fe23-4c95-8e4a-04c7a69a49cd`). El propietario reinició Windows con el reinicio normal y volvió a abrir Claude elevado.

## Resultado (2026-10-10)

`complete-normal-session-reboot-drill.ps1 -RunId 05c03a8e… -OwnerPresent` superó todas las comprobaciones:

- Reinicio observado: arranque nuevo a las 06:31:05Z.
- Servicio Running, Auto, LocalService (PID 5948, arrancado a las 01:31:19); Desktop, ProcessAgent y NativeMessaging iniciados solos.
- **La sesión no se restauró:** estado Inactive, sin SessionId.
- **Exactamente un `SessionInterrupted`**, con resultado **`Stopped`**, a las 06:30:50Z: la parada ordenada de Windows ejecutó el recorder de apagado. `history.db` se escribió a las 01:30:50 local.
- No queda journal.
- Actividad del Host: «gpt test · 1:28–1:30 · 2 min · Sesión interrumpida».
- Sin aviso en el Host: no estaba abierto durante el reinicio, la limitación documentada en FG-405.
- `service-failures.jsonl` sin entradas nuevas.

## Restauración

`desktop-settings.json` restaurado al SHA `987E6001…` con LastWrite 13:19 (la versión del test se guardó en `fg410-desktop-settings-after-test-before-restore.json`); `profiles.db` sin cambios. Estado final: Ready/Ready, Inactive, Strict OFF. Host abierto con `--pilot` y minimizado.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg410-normal-reboot-armed.json`, `fg410-normal-reboot-result.json` y `artifacts\normal-session-reboot\`.

## Pendiente de FG-410

Reinicio con sesión estricta (drill FG-112 sobre .138), cambios de reloj reales, Edge real, DPI 125/150 %, Narrator/teclado/contraste y Windows 11 (`docs/owner-present-acceptance-checklist.md`).
