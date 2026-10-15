# Fase 3 — tick automático con proyección local

## Resultado

`SettingsSyncDesktopTick` envuelve el worker sincronizador. Si el worker no está `Disabled`, vuelve a leer el outbox bajo su lease y proyecta el borrador/snapshot vigente con `DesktopSettingsSyncStore`; así un ACK, un documento remoto o un conflicto se reflejan en el archivo local. La proyección modifica solo tema, idioma y duración; conserva enforcement, perfiles, accesibilidad, notificaciones y demás preferencias. Si el consentimiento está apagado, no abre ni modifica el archivo. Si falla la escritura local, devuelve `SettingsTransportFailure.Storage` para detener el runtime y permitir recovery; no muestra éxito.

La proyección se realiza bajo el mismo orden de leases que usan las ediciones locales (outbox y luego preferencias), para no aplicar un snapshot leído antes de una edición concurrente. El store evita reemplazar el archivo si el JSON final es idéntico, reduciendo escrituras repetidas en polls ociosos.

`SettingsConflictResolutionService` ahora recibe el store de preferencias y sus overrides. Valida revisión, máscara y borrador tal como se revisaron; aplica la elección solo en los campos en conflicto, conserva otros campos locales y persiste outbox/proyección antes de notificar al scheduler. Reset/tombstones tienen su flujo separado.

## Validación

- `SettingsSyncDesktopTickTests`: consentimiento apagado no toca preferencias; fallo de proyección devuelve Storage después de un tick remoto válido.
- Test del servicio de conflictos: confirma proyección antes del siguiente tick, preserva Enforcement/ReduceMotion/ProfileThemes, y no altera el outbox/archivo ante confirmación o borrador obsoletos ni borra tombstones.
- HTTPS pinned de dos dispositivos: cambios de tema/idioma/duración convergen en ambos outboxes y archivos locales; conflicto concurrente se resuelve sobre el archivo del perdedor y luego converge al ganador. Preferencias locales no sincronizadas permanecen intactas.
- Suite completa `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **141/141 PASS**.
- `dotnet format` focalizado en código y tests: PASS.

## Límites

El Host todavía no crea `SettingsSyncDesktopTick` ni `SettingsAutomaticSyncRuntime`, y los guardados reales de `DesktopSettingsStore` aún no emiten señales al scheduler. No se ejecutó contra API staging/Keycloak/Neon ni en dos PCs físicas. Fase 2 sigue abierta por la prueba propietario→hermano; Fase 3 aún no está activa en la aplicación.


Actualización: `SettingsAutomaticSyncCoordinator` añade la entrada local que persiste/proyecta una edición y después emite el wake-up. La prueba más reciente amplía el resultado acumulado de **141/141** a **142/142**; ver `20261014-phase3-local-edit-wakeup.md`.