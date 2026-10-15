# Fase 3: editar un override local durante revisión de reset

## Resultado

El Host conserva la versión más reciente de un override local editado después de mostrar la revisión del conflicto y antes de elegir cómo resolver el reset. La rebase del tombstone no publica la excepción local y proyecta el valor editado tras aceptar.

## Evidencia

`HostResetConflictCanRebaseTheTombstoneOnlyAfterTheReviewedChoice` inicia con overrides de tema/idioma/duración y estado `DraftDeleted` en conflicto. Con la revisión ya instalada en `HostAccountService`, el flujo real de preferencias edita duración local de 90 a 75 mediante `TrySyncSharedSettingsEditAsync`. La prueba comprueba que revisión, draft y conflicto compartidos siguen iguales, que el override DPAPI pasa a 75 y que no hay lecturas ni publicaciones de transporte por esa edición. Al aceptar `ThisDeviceReset`, el tombstone se rebasea, conserva los tres overrides y `DesktopSettings` refleja `dark/es/75`.

El código de resolución recarga el outbox actual y serializa el commit bajo lease; la edición local solo cambia la excepción DPAPI existente. No se modificó el producto distribuible.

## Verificación

- Prueba focalizada: **1/1 PASS**.
- Suite `FocusGuard.Host.Windows.Tests`: **259/259 PASS**.
- Formato de `HostSettingsSyncSessionTests.cs` y `graphify update .`: PASS.
- Sin regenerar instalador, instalar o desplegar.

La aceptación física de conflicto offline, peer y preservación de reglas continúa abierta.
