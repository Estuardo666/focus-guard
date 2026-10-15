# Fase 3 — recuperación de conflicto de reset/tombstone

## Resultado

Un reset concurrente ya no queda indefinidamente en atención. La UI presenta los valores remotos vigentes y los valores por defecto que pide este equipo, y exige elegir una de estas opciones:

- **Mantener el reset de esta PC:** conserva `DraftDeleted=true`, limpia solo el conflicto y rebasea el tombstone sobre la revisión remota observada. El worker prepara después un nuevo tombstone firmado/CAS; no borra el estado de servidor durante la resolución local.
- **Cancelar reset y conservar valores remotos:** descarta solo el borrador de reset local y proyecta el snapshot actual de los otros dispositivos.

Las dos rutas requieren consentimiento explícito, revisión/máscara/draft vigentes bajo el lease del outbox DPAPI y guardado/proyección antes de despertar el runtime. El bridge recibe solo una revisión y una opción tipada; nunca recibe valores de settings. Las reglas, perfiles, permisos, agenda, historial, sesiones y opciones locales quedan fuera de la resolución.

## Archivos

- `cloud/sync/Core/SettingsOutbox.cs` — transición atómica para rebasar reset o aceptar el remoto.
- `cloud/sync/Windows/SettingsConflictResolutionService.cs` — `ResolveResetAsync`, revalidación, proyección y wake-up tras persistir.
- `src/FocusGuard.Host.Windows/Hosting/HostAccountService.cs` — acción Host que contrasta la review con outbox actual y aplica la opción tipada.
- `src/FocusGuard.Host.Windows/Bridge/BridgeRequestParser.cs`, `BridgeContracts.cs`, `NativeBridgeDispatcher.cs` — contrato `account.settings.sync.resolve-reset` con revisión/opción exactas.
- `src/FocusGuard.UI/src/AccountPanel.tsx`, `native/account.ts`, `locales/en.json`, `locales/es.json`, `design-system/zen.css` — comparación y consecuencias visibles.
- `cloud/sync/Tests/SettingsConflictResolutionServiceTests.cs`, `tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncSessionTests.cs`, `AccountBridgeTests.cs`, `src/FocusGuard.UI/src/AccountPanel.test.tsx`, `src/FocusGuard.UI/src/native/account.test.ts` — validación de la recuperación.

## Verificación

- Sync: **143/143**.
- Host Windows: **236/236**.
- UI: **243/243**; `npm run check` pasa.
- Build de UI/renderer y `scripts/verify-dist.mjs` pasan.
- `scripts/test-installer.ps1` pasa.
- Instalador independiente `.168` generado en `artifacts/installer/phase3-reset/FocusGuard-0.1.168-Setup.exe`; el guard de producción pasó, `scripts/test-installer.ps1` pasó y `--package-self-test` devuelve exit code 0. SHA-256 `76F22B9EA6066D7F9B6D7FFBEEC422297FB47638CE0E314557B9F67C2B9B6F4C`; coincide con el Setup incrustado.

No se instaló la versión ni se llamó staging. Sigue pendiente probar sincronización automática real entre dos Windows: consentimiento, edición/convergencia, cierre/reinicio, reconexión y conflicto. La suite HTTPS de dos runtimes sintéticos no sustituye esa aceptación física.
