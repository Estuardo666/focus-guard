# Fase 3 — reparación deliberada del checkpoint de retry

## Resultado

`SettingsRetryCheckpointStore.ResetAfterOwnerRecoveryAsync` permite recuperar un throttle corrupto o temporal huérfano después de una acción deliberada del propietario. Sin `ownerConfirmed: true` no toma el lease ni modifica archivos.

La operación usa el mismo lease OS que load/save/clear, elimina únicamente el archivo de este store y temporales cuyo nombre corresponde al patrón exacto que genera el store (`<archivo>.<GUID-N>.tmp`). Si encuentra más de 32 temporales, se detiene sin borrar. El controlador sigue en `NeedsAttention(Storage)` hasta que el llamador ejecute `ResumeAfterRecoveryAsync`; la reparación por sí sola no reanuda sync.

## Validación

- Prueba de archivo corrupto + temporal huérfano: sin confirmación ambos permanecen; con confirmación se borran y un archivo vecino `.keep.tmp` se conserva.
- Prueba de integración del controlador: no permite un request después de borrar el archivo hasta la reanudación explícita.
- Suite completa `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **134/134 PASS**.
- Formato focalizado de `SettingsRetryCheckpointStore.cs` y `SettingsRetryCheckpointStoreTests.cs`: PASS.

## Implicación de seguridad y limitaciones

El argumento booleano expresa que el llamador ya obtuvo confirmación/recuperación local; el store no realiza reautenticación ni presenta UI. La futura UI debe exponer la reparación solo tras recuperación deliberada. En el mismo proceso, cualquier delay ya observado permanece en memoria al recuperarse. Si el checkpoint estaba corrupto al iniciar el proceso, no existe un Retry-After verificable que pueda restaurarse; la excepción es precisamente el acto explícito de recuperación del propietario.

El runtime preparado aún no está conectado al Host, a una ruta estable/ACL, OIDC, membresía, API ni UI. Fase 2 sigue abierta por la verificación física propietario→hermano.
