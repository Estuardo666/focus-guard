# Fase 3 — recuperación del reintento y reanudación local

## Resultado

Se conectó la reparación deliberada del checkpoint de reintentos al flujo Windows de Fase 3. Si el scheduler detiene la sincronización por daño del checkpoint DPAPI, Host muestra una acción localizada y solicita confirmación nativa. Al aceptar, elimina solo el checkpoint afectado y temporales con el patrón reconocido, vuelve a cargar el estado del controlador y despierta el scheduler ya activo.

El controlador distingue ahora el origen de un fallo `Storage`: una falla de lectura/escritura del retry checkpoint puede habilitar la reparación; una falla de proyección de ajustes no. El scheduler serializa el tick/reporte actual con la recuperación para que un reporte antiguo no vuelva a dejar la UI en atención después de completar el reset.

La prueba de integración Host cubre rechazo y confirmación, conserva byte por byte el outbox DPAPI, preferencias locales y consentimiento, y comprueba que el loop vuelve a hacer una lectura después de borrar el checkpoint. Otra prueba crea y destruye una `HostSettingsSyncSession`, carga otra desde el mismo vault/outbox DPAPI y ejecuta `RunAsync` con consentimiento ya guardado, sin volver a llamar `EnableAsync`.

## Archivos del incremento

- `cloud/sync/Windows/SettingsRetryController.cs`
- `cloud/sync/Windows/SettingsSyncScheduler.cs`
- `src/FocusGuard.Host.Windows/Hosting/HostAccountService.cs`
- `tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncSessionTests.cs`
- `cloud/sync/Tests/SettingsRetryCheckpointStoreTests.cs`
- `cloud/sync/Tests/SettingsSyncSchedulerTests.cs`
- `docs/PLAN-CLOUD-MULTIDISPOSITIVO.md`
- `TASKS.md`

El Setup `.169` además lleva el cableado Host/bridge/UI del recovery y los conflictos por campo/reset que ya se había integrado en este mismo árbol.

## Verificación

- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **238/238**.
- `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --verbosity minimal` — **145/145**.
- `npm test -- --run` en `src/FocusGuard.UI` — **245/245**; `npm run build` y `scripts/verify-dist.mjs` también pasan como parte del build Host.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include cloud/sync/Windows/SettingsRetryController.cs cloud/sync/Windows/SettingsSyncScheduler.cs cloud/sync/Tests/SettingsRetryCheckpointStoreTests.cs cloud/sync/Tests/SettingsSyncSchedulerTests.cs src/FocusGuard.Host.Windows/Hosting/HostAccountService.cs tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncSessionTests.cs` — exit 0.
- `graphify update .` — completó AST de 1,513 archivos; el grafo tiene 23,816 nodos y 159,177 aristas.
- Build local `scripts/build-installer.ps1 -Version 0.1.169` con directorios de payload/setup aislados — PASS. El guard de producción confirmó el endpoint en dos assemblies; verificaciones del Setup confirman que la UI y los recursos embebidos coinciden con el payload.
- `scripts/test-installer.ps1` — PASS. Ejecutable independiente `artifacts/installer/phase3-retry/FocusGuard-0.1.169-Setup.exe --package-self-test` — exit 0.
- Setup independiente y copia del paquete coinciden: SHA-256 `2B6C56386F339C392148ACEE61258EBCD847BD6718BA1EC7E5B899A70A6EA5B3`. ZIP `.169`: `A3D51C58D0336460CFD9D3D46D40929DB9CAE9D27AA08FD93557C852B963EB4C`.

## Seguridad, recuperación y límites

- La acción no activa consentimiento nuevo. Solo puede ejecutarse con sesión verificada, dispositivo vinculado y consentimiento persistido activo, tras confirmar en Windows.
- La reparación no modifica el outbox cifrado, ajustes locales, perfiles, reglas, permisos ni consentimiento. Las rutas de error de protocolo/AEAD/outbox no tienen acceso a esta operación.
- Si el checkpoint recuperado vuelve a fallar o hay temporales desconocidos, el scheduler permanece detenido y la reparación se puede volver a intentar deliberadamente.
- El Setup se generó y validó localmente; no se instaló, desplegó ni llamó a staging.
- Las pruebas cubren sesiones/runtimes locales y transporte de prueba. Sigue pendiente la aceptación física de sincronización automática entre las dos PCs Windows: edición y convergencia, cierre/reinicio, reconexión y conflictos reales.

## Siguiente paso

Cuando estén disponibles ambas PCs, ejecutar la matriz física de Fase 3. Hasta entonces, se puede continuar con pruebas locales de migración y regresión sin afirmar que la convergencia física ya está aceptada.
