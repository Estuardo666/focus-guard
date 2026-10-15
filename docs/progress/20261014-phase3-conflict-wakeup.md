# Fase 3 — resolución explícita y reintento de conflictos

## Resultado

`SettingsSyncWakeReason.ConflictResolved` habilita un wake-up separado para los 30 segundos de espera anti-loop que siguen a un conflicto. `SettingsSyncScheduler.NotifyConflictResolved` y `SettingsAutomaticSyncRuntime.NotifyConflictResolved` deben llamarse después de persistir la resolución. Una edición ordinaria no interrumpe ese intervalo; Retry-After y backoff de transporte tampoco se saltan.

`SettingsConflictResolutionService` ofrece esa secuencia en una sola operación. Exige confirmación, la revisión, máscara de conflicto y borrador exactos revisados por la UI; rechaza una pantalla obsoleta. Aplica la selección solo a campos disputados y preserva otros cambios locales. Los conflictos de reset/tombstone se rechazan para evitar perder la intención de borrado. Primero persiste bajo el lease del vault; solo después emite `ConflictResolved`.

## Validación

- Test del scheduler: una edición local normal no vuelve a ejecutar durante el conflicto; la señal explícita sí lo despierta.
- Test de backoff: conectividad y conflicto resuelto no saltan un `Retry-After` vigente.
- Tests del servicio: confirmación ausente, revisión o borrador obsoletos no alteran el outbox ni despiertan el runtime; preserva un campo local no disputado; no borra tombstones.
- HTTPS con dos dispositivos: ambos cambian concurrentemente duración; CAS acepta una revisión y el perdedor conserva borrador/conflicto. El servicio guarda la elección y el runtime la publica inmediatamente; el ganador converge por HTTPS.
- La prueba HTTPS extendida pasó **5 veces consecutivas**.
- Suite completa `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **139/139 PASS**.
- Formato focalizado de archivos de runtime/tests, modo verificación: PASS.

## Límites

La futura UI aún debe presentar versiones y capturar selección; el servicio no decide por la persona. El ensayo usa tokens/membresía sintéticos en Kestrel localhost, no API staging/OIDC/Neon ni dos PCs físicas. No está conectado al Host ni cambia la app instalada. Fase 2 sigue abierta para la transferencia propietario→hermano.
