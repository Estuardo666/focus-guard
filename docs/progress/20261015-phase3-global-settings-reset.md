# Fase 3 — restablecimiento compartido explícito

## Resultado

Se implementó la acción «Restablecer ajustes compartidos» en Conexión. Solo aparece cuando el dispositivo está vinculado, la sincronización está consentida y el outbox está estable. El bridge acepta el método sin parámetros; los valores no vienen de la UI. Windows presenta los valores compartidos actuales y los defaults de destino antes de pedir una confirmación.

Tras aceptar, `HostAccountService` envía la vista previa —revisión y valores actuales— a `HostSettingsSyncSession`. Bajo el lease DPAPI, `LocalSettingsCoordinator` vuelve a comprobar consentimiento, revisión, valores, ausencia de draft/in-flight/conflicto y estado no eliminado. Si hubo cambios mientras el diálogo estaba abierto, rechaza el reset y conserva el draft reciente. Si sigue vigente, escribe el tombstone mediante `SettingsOutbox.RequestReset`, proyecta solo tema/idioma/duración predeterminada y despierta el scheduler después del commit.

El tombstone no altera snapshots de sesiones activas. Enforcement, perfiles, reglas, permisos, agenda, historial, `ReduceMotion` y notificaciones siguen locales. La acción no puede alterar el consentimiento ni un conflicto en curso.

La prueba Host del flujo de reset valida preview/confirmación y frescura; la integración HTTPS ejerce su propagación: tras la edición y el conflicto de duración, la sesión Host del propietario solicita el reset en la revisión vigente, el segundo dispositivo recibe el tombstone al reconectarse y aplica defaults a sus tres campos globales, preservando preferencias locales.

Después de la convergencia, ambos loops/composiciones Host se cierran. La prueba vuelve a cargar las identidades desde los vaults DPAPI y crea sesiones nuevas sobre el mismo endpoint TLS, sin llamar `EnableAsync`. `RunAsync` confirma el consentimiento persistido, relee el tombstone por HTTPS, conserva la revisión 4 sin publicar de nuevo y proyecta defaults manteniendo las preferencias locales de ambos equipos.

La prueba continúa desde esa reapertura: el propietario publica un valor nuevo en revisión 5, deja preparado un reset offline y el peer publica idioma distinto en revisión 6. Al reanudar, el tombstone detecta ese cambio concurrente. Se resuelve con la acción Host «Mantener el reset de esta PC», que rebasea el tombstone; el servidor acepta la revisión 7 y el peer aplica el reset, manteniendo consentimiento y preferencias locales.

## Verificación

- `HostAccountServiceResetPreviewsCancelsAndPublishesOnlyTheSharedSettingsTombstone`: verifica el preview actual→default, cancelación byte por byte del outbox/preferences sin solicitudes, rechazo de un preview obsoleto después de una edición concurrente, y aceptación que conserva consentimiento, crea tombstone, proyecta solo los tres campos y despierta el siguiente tick.
- Host: `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --verbosity minimal` — **240/240**.
- Sync: `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore --verbosity minimal` — **145/145**.
- UI: `npm test -- --run` en `src/FocusGuard.UI` — **246/246**; `npm run check`, Vite build y `scripts/verify-dist.mjs` pasan dentro del build Host.
- `TwoHostSessionsConvergeOverPinnedHttpsAndPreserveDeviceLocalPreferences` incluye edición, conflicto CAS/resolución Host, tombstone cross-device, reanudación DPAPI de ambas sesiones y conflicto/reset CAS resuelto por `HostAccountService`; 3/3 ejecuciones verdes.
- `dotnet format whitespace FocusGuard.slnx --verify-no-changes --no-restore --include cloud/sync/Windows/LocalSettingsCoordinator.cs cloud/sync/Windows/SettingsAutomaticSyncCoordinator.cs src/FocusGuard.Host.Windows/Hosting/HostSettingsSyncSession.cs src/FocusGuard.Host.Windows/Hosting/HostAccountService.cs src/FocusGuard.Host.Windows/MainForm.cs src/FocusGuard.Host.Windows/Bridge/BridgeRequestParser.cs tests/FocusGuard.Host.Windows.Tests/AccountBridgeTests.cs tests/FocusGuard.Host.Windows.Tests/HostSettingsSyncSessionTests.cs` — exit 0.
- `graphify update .` — AST de 1,542 archivos; 23,898 nodos y 160,641 aristas.
- `scripts/build-installer.ps1 -Version 0.1.170` con directorios aislados — PASS. `Assert-FocusGuardProductionClient.ps1` verificó el endpoint de producción en ambos assemblies del Client. `scripts/test-installer.ps1` pasó; el Setup `--package-self-test` salió con código 0.
- Setup standalone y copia del paquete coinciden por SHA-256: `A1FAE72E7CA0C60A3D4A97B574452602145DC8CBA399879E44533205D3A537C7`. ZIP `.170`: `C8E442DD8555D5E99F361EA36DDE54300471BF88A8C96DA82026345075EECDFF`.

## Seguridad y límites

- El usuario confirma en un diálogo nativo después de ver los valores exactos actuales y de destino.
- El reset solo puede iniciar con consentimiento persistido activo y un documento sin cambios pendientes ni conflictos.
- No se incluyen valores arbitrarios ni parámetros de rutas en el bridge. La operación cambia solo el documento cifrado global; las reglas locales no se transmiten.
- El Setup `.170` fue generado y validado, no instalado ni desplegado. No se llamó a staging.
- Las pruebas son locales y usan Kestrel, identidades y tokens sintéticos. Recrear las sesiones no sustituye cerrar/reabrir el proceso ni reiniciar físicamente Windows. Sigue pendiente aceptar la sincronización automática en las dos PCs físicas, incluida aplicación del tombstone y resolución de conflicto de reset en equipos reales.

## Siguiente paso

Continuar con la [aceptación física en ambos Windows](../cloud-phase0/PHASE3-PHYSICAL-ACCEPTANCE.md): lanzamiento, login/vínculo, edición/convergencia, cierre/reinicio, reconexión y conflictos de duración/reset.
