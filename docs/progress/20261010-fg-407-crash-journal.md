# FG-407 — sesión normal perdida por caída o kill del servicio

## Plan

FG-405 solo cubría la parada ordenada. Para cubrir una caída o un kill sin cambiar la regla «normal no persiste»: el servicio mantiene un journal acotado de la sesión normal en curso; un journal huérfano al arrancar se registra como interrumpido. El SCM reinicia el servicio tras un fallo para que el Host pueda avisar.

## Cambios

- `NormalSessionJournal`: `ProgramData\FocusGuard\normal-session-journal.json`, esquema v1, máximo 64 KiB, escritura atómica (tmp + move). Valida GUIDs, offsets UTC, inicio ≤ último latido ≤ inicio + 31 días, minutos, como máximo 256 fases dentro del intervalo, métricas e intención. Un archivo inválido se mueve a `.invalid-<UTC>` y nunca se convierte en historial. ACL heredada: escritura solo para SYSTEM, LocalService y Administradores.
- `IFocusService.CreateInterruptionJournalEntry` / `RecordOrphanedInterruptionAsync`. El cierre se refactorizó en `BuildClosureEntryNoLock`. La reconciliación es idempotente: si el SessionId ya está en el historial, no hace nada.
- `NormalSessionJournalWorker`: reconcilia en `StartAsync`, sondea cada 1 s, escribe al cambiar de sesión o cada 10 s y borra al terminar. En `StopAsync` (después del recorder) elimina el journal. Los errores de E/S van a `service-failures` sin detener el servicio.
- Audit `SessionInterrupted`: resultado `ServiceTerminated` (huérfano) o `Stopped` (parada ordenada).
- Instalador: `sc.exe failure FocusGuard reset= 86400 actions= restart/5000/restart/30000//`, con un contrato añadido en `test-installer.ps1`.
- `SPEC.md` y `THREAT_MODEL.md` actualizados.

## Pruebas

`NormalSessionJournalTests` (11 casos): ida y vuelta; cuarentena de JSON corrupto, esquema desconocido y fuera de límites; heartbeat; reconciliación única en el último latido con audit `ServiceTerminated`; la parada ordenada no deja journal; una sesión estricta nunca se registra en el journal. .NET Debug 909/909 (con FG-409); React 147/147.

## Aceptación instalada

- **.137 (`fg407-service-kill-journal-raw.json`):**
  - Bloqueo previo auditado; journal con último latido 05:42:22Z y ACL SYSTEM/LocalService/Admins.
  - `Stop-Process -Force` del servicio a las 05:42:27; el SCM lo reinició 5,7 s después.
  - Estado Inactive, audit `ServiceTerminated` con el mismo SessionId, journal borrado.
  - Aviso del Host; Actividad muestra «Sesión interrumpida»; bloqueo liberado.
- **.138 (`fg409-service-kill-journal-138-raw.json`):** mismo resultado. Al ser el segundo fallo en 24 h, el SCM aplicó la acción de 30 s (reinicio a los 31 s). El bloqueo previo consta en el audit a las 05:53:25Z; el script lo marcó false porque el lanzamiento vía explorer tardó unos 12 s.

## Limitaciones

- El fin registrado es el último latido (hasta 10 s antes de la caída).
- **Tercer fallo en 24 h: el SCM no reinicia** (evita bucles), así que el registro y el aviso llegan en el siguiente arranque. Tras estas pruebas el contador de fallos de este equipo va por 2 hasta que pasen 24 h sin fallos.
- Si el proceso muere antes del primer latido (menos de 1 s tras activar), no hay registro.
