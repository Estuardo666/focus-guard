# FG-317 — Preflight de rollback versionado

Fecha: 2026-10-08

Estado: preflight completado sin cambios; rollback real pendiente.

## Cambio de herramienta

`scripts/invoke-installer-rollback-drill.ps1` ya no apunta en código a `artifacts/installer/FocusGuard-0.1.0`. Ahora recibe `-PackageDirectory` explícito, valida el manifest y todas sus rutas bajo esa raíz, confirma que la versión candidata es posterior a la instalada y exige escoger exactamente `-PreflightOnly` o `-Execute`. La rama destructiva requiere `-OwnerPresent` y un PowerShell que ya tenga token administrador; la herramienta nunca se autoeleva.

## Ejecución read-only

Preflight contra `artifacts/fg315-output/package/FocusGuard-0.1.104`:

- versión instalada .102; candidata .104; 31 archivos del manifest presentes;
- Service `Running`, LocalService, PID25096;
- DrillClient `serviceReady=true`, sesión `Expired/0`, Strict desactivado;
- `InstallerInvoked=false`, `MachineStateChanged=false`.

El JSON queda en `artifacts/fg315-output/fg317-rollback-preflight.json`. `scripts/test-installer.ps1` pasa e incluye parse/guardas del rollback drill.

## Límite

No se ejecutó `-Execute`: esa ruta inicia el Desktop, llama al instalador con `-InjectFailureAfterCopyForRollbackTest` y modifica temporalmente archivos/registro bajo Program Files para comprobar restauración. La consola de este ejecutor es non-admin. El helper queda preparado para la prueba owner-present, pero el preflight no demuestra rollback. FG285 sólo prueba la transición histórica .98→.97; .102→.102 conserva su gate.
