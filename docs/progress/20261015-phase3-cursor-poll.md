# Fase 3 — reconciliación condicional por cursor

## Resultado

El API expone `GET /v1/sync/cursor`. La ruta comprueba la misma membresía de cuenta, sesión activa del dispositivo y prueba firmada que las lecturas de documentos. Devuelve el cursor monotónico como JSON y `X-FG-Cursor`.

El cliente consulta primero este cursor. Si coincide con el cursor guardado en el outbox DPAPI y no hay borrador, operación en vuelo ni conflicto que resolver, el tick termina en `Idle` sin descargar ni descifrar el documento E2EE. Si el cursor avanzó, hay trabajo local o existe una condición pendiente, sigue la reconciliación completa por `/v1/documents`. Las respuestas de una API anterior sin la ruta nueva (`404`) activan el GET completo, conservando compatibilidad. Los cursores discrepantes o retrocedidos siguen fallando de forma cerrada.

## Verificación

- `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --verbosity minimal` — **150/150**. La nueva regresión recorre el mismo tick del worker: `/v1/sync/cursor` devuelve 404, el transporte firmado cae al GET real de `/v1/documents/{id}`, y el snapshot pasa verificación E2EE y se guarda en el outbox.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **240/240**.
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres -Phase3SyncOnly` — **20 checks** para cursor, fallback de API anterior y Host a través del router real.
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres` — **145 checks** (125 OIDC/API/DPAPI + 15 runtime/ledger + 5 Host/API-router). Los dos Host consultan cursor y convergen por las rutas HTTP `/v1/sync/cursor` y `/v1/documents`; el Host usa bearer/userinfo sintéticos e issuer HTTPS. Los 125 checks separados recorren Keycloak/JWT reales.
- `dotnet build cloud/identity/Tests/FocusGuard.Identity.Tests.csproj -c Release --no-restore --verbosity minimal` compila la solución de integración sin warnings ni errores; UI246/246, `npm run check`, Vite y `scripts/verify-dist.mjs` pasan dentro del build del Host.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include ...` y el parseo de `cloud/deploy/Test-KeycloakNeon.ps1` pasan.
- Setup `.173` y ZIP generados en `artifacts/installer/phase3-cursor-poll`; guard de cliente de producción, `scripts/test-installer.ps1` y `--package-self-test` pasan. Setup SHA-256 `2B4A3E44D00CF128EB28C3BD317C0F023D37EF95FF5DC2683EC330F40D072504`; ZIP SHA-256 `088C6F2FD3557CFA13118E90A10FB0877FB21E314B3F21F732AB650A5D4AD01B`.
- Las pruebas usan PostgreSQL/Keycloak locales efímeros. No se accedió a Neon/staging y no se instaló ni desplegó `.173`.

## Límite

El sondeo de cursor reduce tráfico solo con un backend que implemente el endpoint; ante un servidor anterior, el cliente hace fallback a la lectura completa. Falta aceptar convergencia y reanudación automática con dos Windows físicos; la prueba contra staging se conserva después de ese gate. Ver el [runbook de aceptación física](../cloud-phase0/PHASE3-PHYSICAL-ACCEPTANCE.md).
