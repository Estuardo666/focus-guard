# FG-385 — equivalencia de edición de perfiles/reglas a 800×600

## Preflight
- Host instalado .134, usuario medio con `--pilot`; confirmar ServiceReady/ExtensionReady, SessionState Expired y Strict apagado.
- Query SQLite en modo ReadOnly: guardar schema, cinco perfiles, cinco reglas de aplicación, treinta reglas web, nombres/counts y `UpdatedUtc`; hash de desktop-settings.

## Prueba
- Abrir Perfiles en la ventana 800×600.
- Crear `FG-385 prueba temporal`; añadir manualmente Block para `FG385-NeverRun.exe` y Block para `example.invalid`; verificar que los conteos visibles cambian.
- No iniciar sesiones ni lanzar el ejecutable/navegador; enforcement no se aplicará.
- Borrar solo el perfil temporal, volver a seleccionar `2`, y comparar con el snapshot; restaurar desktop-settings byte por byte si su hash cambia.
- Cerrar/restaurar ventana a 1216×799 y confirmar servicio Expired/Ready.

## Riesgo y aceptación
La escritura temporal afecta solo un perfil nuevo que se eliminará inmediatamente; no se toca ningún perfil/regla existente. El perfil de prueba no se ejecuta ni se usa en una sesión. Aceptar si reaparecen los 5 perfiles, 5 reglas de aplicación y 30 sitios originales, `UpdatedUtc` de perfiles existentes sigue igual y Preferencias conserva el SHA previo.
## Resultado (2026-10-09)
- Host .134 probado en ventana 800×600. Creé `FG-385 prueba temporal`, guardé Block para `FG385-NeverRun.exe` (coincidencia por nombre) y Block para `example.invalid`. No inicié ninguna aplicación, sesión ni enforcement.
- La confirmación de borrado nombró exactamente `FG-385 prueba temporal`; eliminé ese perfil temporal.
- Post-estado SQLite read-only: schema3, 5 perfiles, 5 reglas de aplicación y 30 sitios. Los cinco perfiles existentes conservan sus reglas y `UpdatedUtc`; `desktop-settings.json` mantiene el SHA original `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55`.
- Diagnostic: ServiceReady/ExtensionReady true, `Expired`, Strict false y `enforcementConnected=false`. Host restaurado a Resumen/1216×799, servicio conectado y extensión disponible.
- Evidencia en `D:\TEMP\FocusGuard-FG385-profile-edit-20261009\`: snapshots before/after, registro UI y capturas del rule test y Resumen final.