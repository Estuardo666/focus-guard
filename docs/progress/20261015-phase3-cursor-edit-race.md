# Fase 3 — edición confirmada durante el polling del cursor

## Resultado

Se añadió una regresión para el límite entre el sondeo ligero del cursor y la lectura del outbox. La prueba detiene `ReadCursorAsync`, persiste un cambio de tema en el outbox DPAPI mientras la respuesta está en vuelo y luego devuelve el mismo cursor que la cuenta ya tenía guardado.

El worker vuelve a leer el outbox después del polling: detecta el borrador aunque el cursor no haya avanzado, descarga el snapshot verificado, publica la escritura CAS y confirma por lectura del documento cifrado. El estado final contiene el tema nuevo en la revisión siguiente y no queda pendiente. La optimización de cursor no oculta cambios locales confirmados durante la solicitud.

## Verificación

- `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~EditCommittedDuringCursorPollIsReconciledInsteadOfSkipped --verbosity minimal` — **1/1**.
- `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --verbosity minimal` — **151/151**.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include cloud/sync/Tests/SettingsSyncWorkerTests.cs` — PASS.
- Solo cambió cobertura; no se regeneró ni instaló `.173`, y no se llamó a staging.

## Límite

La regresión usa el transporte controlado del test para fijar el interleaving. El scheduler ya prueba por separado que conserva señales recibidas durante un tick activo; la aceptación física en dos PCs sigue siendo necesaria.
