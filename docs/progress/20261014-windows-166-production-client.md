# Windows .166 — endpoint de producción confirmado

## Causa

La preparación ad hoc del hotfix .164/.165 compiló Client en Release sin `FocusGuardProduction=true`. `IpcProtocol.PipeName` es un `const` condicional que queda insertado en el constructor del consumidor. El Client .165 buscaba `FocusGuard.Development.Control.v2`, mientras el servicio instalado escucha `FocusGuard.Control.v2`. Ninguna espera de readiness podía corregir esa diferencia. La hipótesis previa de arranque lento queda supersedida para este fallo.

Se comprobó contra los binarios reales de ambos paquetes: el verificador aislado carga Client y sus dependencias, crea el cliente sin conectarse y lee el endpoint predeterminado. .165 devuelve Development (exit 1); .166 devuelve producción (exit 0). Los hashes del Client instalado restaurado coinciden con la versión previa de producción y el health check real tras rollback devuelve Ready/0.

## Cambio

- Hotfix .166 basado en .165, preservando Host/React/servicio .163 y la corrección de preferencias.
- Client compilado con `--property:FocusGuardProduction=true`; SHA-256 `8E298EC6A92F7BA9A739D9FFC60789CC33395EA95114754C1A8E5201ABBCAF28` en Host y DrillClient.
- `scripts/Assert-FocusGuardProductionClient.ps1` rechaza canales Development y exige el canal de producción en todas las copias Client. Contempla ambas alineaciones posibles de los strings UTF-16 del heap CLR.
- `scripts/build-installer.ps1` ejecuta la guarda después de publicar y antes de generar el paquete. `test-installer.ps1` valida el script y que el build lo invoque.
- Se mantienen rollback y verificación autenticada. No se cambiaron ACL, identidad, permisos ni requisitos de salud.

## Pruebas

- Client Release/producción: 112/112 (`dotnet test tests/FocusGuard.Client.Tests/FocusGuard.Client.Tests.csproj -c Release --property:FocusGuardProduction=true --no-restore --verbosity quiet`).
- Guarda contra payload real .165: rechazo esperado por canal Development.
- Guarda contra payload real .166: dos copias Client verificadas.
- `artifacts/blank-window-hotfix/endpoint-probe`: .165 exit 1 y .166 exit 0, leyendo el endpoint del objeto real en un AssemblyLoadContext aislado. No abre conexiones ni escribe estado del producto.
- `scripts/test-installer.ps1`: PASS, incluidos ocho casos de readiness, verificación de token de usuario real y paridad de archivos de extensión.
- Standalone `.166 --package-self-test`: exit 0; Host publicado `--smoke`: exit 0.
- Script y dos Client embebidos contrastados con el payload en `build-production-hotfix.ps1`.
- Servicio instalado tras rollback: `--service-health-check` exit 0. No se instaló .166 desde esta sesión sin elevación.

## Aceptación en la PC del propietario — 14 de octubre

El propietario informó que reinstaló/actualizó FocusGuard y compartió una captura del panel de conexión: aparece la sesión iniciada, el correo verificado, este Windows vinculado a la cuenta y la privacidad E2EE. La verificación local de solo lectura confirmó que `C:\Program Files\FocusGuard\FocusGuard.Client.dll` tiene SHA-256 `8E298EC6A92F7BA9A739D9FFC60789CC33395EA95114754C1A8E5201ABBCAF28`, igual al Client de producción .166; el servicio FocusGuard está Running y `FocusGuard.UI.exe --service-health-check` terminó con código 0. Esto acepta la instalación y salud en la PC del propietario, pero no verifica la PC del hermano ni la transferencia física entre ambos equipos.

## Artefactos

| Archivo | Bytes | SHA-256 |
|---|---:|---|
| `artifacts/installer/FocusGuard-0.1.166-Setup.exe` | 371512232 | `B20DD3142F020B0A61643D672FC1F2F1DD2A587CA42FE89E38D4465C49F572DA` |
| `artifacts/installer/FocusGuard-0.1.166-win-x64.zip` | 552310857 | `68DBDC435FB84C73DA02BAFB546CB45B4ECFF39D16433FEB02D61893727E490D` |

Construcción y procedencia: `artifacts/blank-window-hotfix/build-production-hotfix.ps1`, `payload-provenance-166.json`, `hashes-166.json`, `installer-tests-166.log`.

## Límite y siguiente paso

La instalación .166 en la PC del propietario está aceptada. La PC del hermano y la prueba física propietario→hermano siguen pendientes; no usar .164/.165 como candidatos funcionales. Fase 2 continúa abierta y la integración Host/UI de Fase 3 permanece en desarrollo y excluida del hotfix.
