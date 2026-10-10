# FG-409 — robustez de reloj del journal, drill de reinicio normal y checklist del propietario

## Hallazgo y corrección

Con el reloj de pared retrasado por debajo del inicio de la sesión, la entrada del journal quedaba inválida (fin < inicio), y el worker habría escrito en `service-failures` cada segundo. Un retroceso parcial además hacía retroceder el último latido y perdía tiempo de enfoque ya registrado.

- `CreateInterruptionJournalEntry` acota la hora al intervalo [inicio, fin planificado].
- El worker solo reescribe si el fin avanza; el heartbeat usa tiempo monotónico (`TimeProvider.GetTimestamp`).
- Pruebas: `ClockRollback_NeverRewindsOrInvalidatesTheJournal` y `ClockJumpForward_IsClampedToThePlannedEnd`.

## Drill de reinicio con sesión normal

`scripts/arm-normal-session-reboot-drill.ps1` (preflight y armado; nunca reinicia, exige `-OwnerPresent` y contexto ya elevado) y `scripts/complete-normal-session-reboot-drill.ps1`. Verifican un reinicio observado, el servicio Auto/LocalService, que la sesión **no** se restaura, exactamente un `SessionInterrupted` (`Stopped` o `ServiceTerminated`) y que no queda journal. Sin errores de parseo; el preflight sin sesión responde Ready=false con su motivo. **El reinicio real está pendiente del propietario.**

## Checklist del propietario

`docs/owner-present-acceptance-checklist.md`: reinicio normal y estricto (FG-112), cambios de reloj, Edge, DPI, Narrator/teclado/contraste y Windows 11. Son las pruebas que el agente no puede hacer: cortan su sesión, cambian la configuración del sistema o requieren otra máquina o la sesión del propietario.

## Instalador .138

.NET Debug 909/909 (0 warnings), React 147/147, `test-installer`, paquetes Chrome/Edge .138, Setup `--package-self-test` exit 0. `FocusGuard-0.1.138-Setup.exe` mide 442,915,000 bytes, SHA `8525B81D4021638CD81725C885D7762A055F62F6C1B2FD372F92793C929FC56E`. Instalado con exit 0; Service.dll coincide; recuperación SCM presente; repetida la prueba de kill (FG-407).

## Estado final

Ready/Ready Inactive, Strict OFF; settings restaurados al SHA `987E…`; profiles.db sin cambio; sin TestTarget ni journal; `service-failures` sin entradas nuevas; Host abierto en modo piloto y minimizado.
