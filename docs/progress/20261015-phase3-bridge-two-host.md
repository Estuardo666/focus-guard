# Fase 3 — edición de preferencias desde el bridge a dos Hosts

## Resultado

Se cerró una brecha entre pruebas que antes cubrían la UI nativa y la convergencia por separado. La prueba dual-host sobre HTTPS ahora envía una llamada `preferences.set` al `NativeBridgeDispatcher` real, conecta el comando con un `HostAccountService` real y lo hace actualizar la sesión de sincronización consentida. El peer recibe el documento cifrado y aplica tema, idioma y duración predeterminada.

La operación cambia los tres campos compartidos en una sola revisión. El test verifica la respuesta del bridge, el commit local, el outbox confirmado y la convergencia al segundo dispositivo. `ReduceMotion`, notificaciones de ciclo y enforcement permanecen locales, conservando valores diferentes entre PCs.

La prueba reutiliza el fixture HTTPS de dos Hosts y su servidor de prueba con proofs, sesiones, CAS y documentos E2EE; no es staging ni sustituye la prueba física. El `HostAccountService` restaura la sesión de sync creada por el fixture, para enfocar esta prueba en la frontera puente→servicio→sync.

## Verificación

- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TwoHostSessionsConvergeOverPinnedHttpsAndPreserveDeviceLocalPreferences --verbosity minimal` — **1/1**.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **240/240**.
- `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --verbosity minimal` — **150/150**.
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres` — **145 checks** con Keycloak/JWT real, PostgreSQL efímero, API y DPAPI; los contenedores del harness se limpian.
- `cloud/deploy/Test-ApiContainerSmoke.ps1` — construye la imagen Docker del API, la ejecuta junto a PostgreSQL efímero, verifica `/health/live` 200, `/health/ready` 200 y `GET /v1/sync/cursor` sin bearer 401, y limpia contenedores, volumen e imagen temporal.
- El parser de PowerShell no reporta errores en `cloud/deploy/Test-ApiContainerSmoke.ps1`.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncHttpsTests.cs` — PASS.

No cambió código de producto ni contenido del instalador `.173`; no se instaló ni desplegó software y no se llamó a staging.

## Límite

La prueba valida una frontera adicional de la edición local, pero todavía usa dos Hosts de prueba y un servidor HTTPS loopback. La convergencia automática con las dos PCs físicas y los conflictos reales de red siguen pendientes según el [runbook de Fase 3](../cloud-phase0/PHASE3-PHYSICAL-ACCEPTANCE.md).
