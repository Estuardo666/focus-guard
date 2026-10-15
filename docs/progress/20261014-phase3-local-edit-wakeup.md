# Fase 3 — wake-up tras una edición local duradera

## Resultado

`SettingsAutomaticSyncCoordinator` envuelve `LocalSettingsCoordinator`. `EditAsync`/`EditSelectedAsync` esperan a que el outbox y la proyección a DesktopSettings se guarden, y solo después llaman a `NotifyLocalSettingsChanged`. `ReapplyAsync` vuelve a proyectar el estado durable sin intentar una sincronización.

El consentimiento sigue siendo la barrera del runtime: una edición con sync apagado queda en el outbox y se refleja en preferencias locales, pero no llama al worker ni al transporte. Cuando la persona activa la sincronización, el primer tick observa ese borrador. Si el commit o la proyección fallan, la excepción impide emitir el wake-up.

## Validación

- Test con consentimiento apagado: edit seleccionado dura/proyecta, se notifica el scheduler pero no hay worker; el opt-in explícito posterior despierta el runtime y el tick encuentra el borrador correcto ya persistido.
- Suite completa `dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --no-restore`: **142/142 PASS**.
- `dotnet format` focalizado del adaptador y su test, modo verificación: PASS.

## Límites

El Host todavía no enruta sus guardados existentes a este adaptador; siguen faltando observación/integración real de cambios, consentimiento UI, cursor y servicios OIDC/membresía/API en la app. La prueba no toca staging ni instala una versión. Fase 2 continúa abierta por la transferencia física propietario→hermano.
