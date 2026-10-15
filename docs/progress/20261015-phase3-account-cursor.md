# Fase 3 — cursor account-wide durable

## Resultado

`ContentDocumentLedger` mantiene `focusguard_device.sync_cursors`, una secuencia monotónica por cuenta que no forma parte del contenido E2EE ni sustituye la revisión CAS/ETag del documento. La migración crea entradas para cuentas existentes a partir de la revisión más alta disponible. Cada escritura CAS incrementa el cursor en la misma transacción que guarda el ciphertext; las lecturas y 404 autenticados devuelven el valor en `X-FG-Cursor` y, cuando hay documento, también en el snapshot.

`SettingsDocumentHttpTransport` acepta cursores opcionales para mantener compatibilidad con servidores anteriores, comprueba que el header y el cuerpo coincidan y rechaza un retroceso. `SettingsSyncWorker` persiste el cursor observado junto con el estado DPAPI del outbox. Un cursor menor que el guardado falla cerrado.

## Verificación

- `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --verbosity minimal` — **147/147**, incluidos header/cuerpo discordantes, rollback y persistencia del cursor en el outbox DPAPI.
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres -Phase3SyncOnly` — **18 checks** de runtime, Host/API-router y cursor sobre PostgreSQL efímero.
- `cloud/deploy/Test-KeycloakNeon.ps1 -NativeOnly -LocalPostgres` — **143 checks**: 125 de OIDC/API/DPAPI más 18 de sincronización/cursor/Host.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **240/240**.
- Setup `.172` y ZIP generados en `artifacts/installer/phase3-cursor`; guard de cliente de producción, `scripts/test-installer.ps1` y `--package-self-test` pasan. Setup SHA-256 `FF86A5FBFB4CCED43796D7B7DD0466308A8234D0E53099BA006E820A2B1F2714`; ZIP SHA-256 `D32649213F09F2CBA43BEC5A791B47C94E719A2D515EF1FE83F1E12A08D8A9EC`.
- No se instaló el paquete, no se contactó Neon/staging y no quedaron contenedores ni volúmenes temporales.

## Límite

El cursor es opcional en el protocolo para que la app siga funcionando con APIs anteriores, pero estas no ofrecen la señal hasta que el backend se despliegue. La aceptación de sincronización automática en dos PCs Windows y la validación contra el backend actualizado siguen pendientes; staging no se modifica antes del gate físico.
