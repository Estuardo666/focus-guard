# FG-382 — plan: el listado de perfiles no debe escribir SQLite

## Reproducción y evidencia
Tras la comprobación read-only de Perfiles en FocusGuard .132, `profiles.db` pasó a LastWrite 3:14:31 PM. Los datos consultados siguen mostrando 5 perfiles, 5 reglas de aplicación, 30 reglas web y `UpdatedUtc` antiguos. La UI no accionó controles de escritura. En un SQLite aislado con schema v3, ejecutar de nuevo `PRAGMA user_version=3` con el mismo valor cambió LastWrite.

## Plan
- Separar en `EnsureSchemaAsync` el pragma de conexión `foreign_keys=ON` de los pasos de migración.
- Consultar `PRAGMA user_version` antes de DDL; para v3 vigente, devolver sin DDL, seed ni `user_version=`; para versiones futuras, fallar de forma explícita; mantener la migración aditiva v0–v2.
- Agregar prueba Storage que inicializa una base real, fija un LastWrite sentinel, llama `ListProfilesAsync` y verifica datos y timestamp intactos.
- Ejecutar Storage y solución Debug, `npm test`/build relevante si aplica, `test-installer`, generar Setup .133 en D, validar manifest/Setup self-test, instalar y verificar `profiles.db` tras el listado real de Perfiles.

## Riesgo/recuperación
El cambio evita reescrituras de metadatos de esquema en el camino de lectura; no cambia datos de perfil ni reglas. La prueba usa una base temporal. En el producto instalado, `profiles.db` puede actualizarse una sola vez si una base v0–v2 necesita la migración aditiva; la base v3 actual debe permanecer sin cambios. No se permite degradar una base creada con una versión de esquema más nueva que este cliente.
## Resultado y aceptación
`SqliteProfileStore.EnsureSchemaAsync` ya habilita `foreign_keys` en cada conexión, lee `PRAGMA user_version` y retorna sin DDL, seed ni escritura cuando la base ya es v3. Las versiones mayores se rechazan explícitamente; las versiones viejas mantienen la migración aditiva y luego se marcan v3. La raíz del timestamp fue reproducida en SQLite aislado con `PRAGMA user_version=3` repetido.

- Test Storage de no escritura por LastWrite sentinel y test de protección de schema futuro: incluidos. Storage 42/42.
- Solución Debug serial `dotnet test FocusGuard.slnx --no-build --no-restore -m:1`: 891/891. El intento de solución paralelo terminó exit1 por `NullReferenceException` de limpieza `PortableSymbolReader` en el adaptador xUnit; la suite serial completa pasó.
- Installer .133 Release: Setup/ZIP y extensiones Chrome/Edge verificados; npm extension19/19, `test-installer.ps1`, Setup `--package-self-test` exit0, manifest31/31. Setup SHA `E09F3062A5551F4DFE4E945280C2C3443B080DD48B74AE8FEAB0C218169EB698`; ZIP SHA `1172A5B549F3BEB483EE13D89C67EEB007BED7B1B4458AE168FDB84C7F1135E2`.
- Instalación autorizada .133: exit0; health check autenticado pasa con servicio LocalService; 22/22 archivos `publish` instalados coinciden por hash; Host dueño/medio `--pilot`, conexión y extensión disponibles, sin sesión activa.
- Comparación read-only antes/después de instalar y abrir Perfiles: `profiles.db` 61,440 bytes, `user_version=3`, 5 perfiles, 5 reglas de aplicación, 30 reglas web y mismo LastWrite `2026-10-09T20:14:31.1250109Z`; `UpdatedUtc` más reciente se mantiene en 2026-10-03. `desktop-settings.json` no cambió.
- Evidencia de base de datos: `D:\TEMP\FocusGuard-FG382-validation-20261009\preinstall-database-state.json`. El probe SQLite sintético vive en `D:\TEMP\FocusGuard-FG382-sqlite-probe-20261009\probe.db`; no contiene datos del usuario. Los artefactos de build .133 permanecen en E; no se movieron ni borraron archivos.

## Próximo gate
Aceptar edición/guardado de perfiles y reglas en la UI .133, probar continuidad de sesión activa con Service/Host .133 y completar los demás pendientes de Fase 8/9. El cambio no cierra las matrices IPC, DPI/accesibilidad, Edge, tray/recovery/rollback ni habilita Fase10.