# Windows .165 — pantalla en blanco y disponibilidad del servicio

## Resultado

Se generó un hotfix autocontenido .165 desde el payload .163 verificado. Conserva Host, React, servicio y controles de identidad de esa versión. Sustituye `FocusGuard.Client.dll`/PDB en Host y DrillClient por la compilación con el acceso de preferencias corregido, actualiza el script del instalador y versiona la distribución de extensión/manifiesto. La integración automática Host/UI de Fase 3 que existe en el árbol de trabajo sigue excluida de este paquete.

## Causa y alcance de la evidencia

La instancia instalada .163 se observó en `about:blank`, antes de navegar a la aplicación. El helper `DesktopSettingsFileLease` capturaba `WindowsFormsSynchronizationContext` en sus awaits aunque `DesktopSettingsStore.Load/SaveVerified` los esperaba sincrónicamente. Se reprodujo un deadlock forzando contención del lease en un hilo STA con contexto Windows Forms: timeout antes de corregir; carga y guardado pasan después. Se evitó la captura de contexto en reintento/lectura/escritura/reemplazo y se usa cierre síncrono del FileStream para evitar captura en disposal implícito. No se borró el perfil del navegador ni datos de identidad.

La instalación física .164 falló con ExitCode 12 / Exchange / TaskCanceledException. El rollback restauró los hashes Client/Host .163. Se observaron temporalmente servicio `Running` sin pipes, posterior cambio de PID y presencia de los tres pipes; la misma prueba autenticada finalmente pasó Ready/0, repetida a las 23:19 UTC. La causa interna del reinicio no se pudo inspeccionar: ProgramData requiere administrador y esta sesión no está elevada. La línea de rechazo del 11 de octubre impresa por Setup era histórica; no demuestra una denegación nueva.

El nuevo instalador admite como máximo tres comprobaciones completas, repitiendo únicamente Exchange=12 o NotReady=14. Bootstrap=11 y códigos desconocidos fallan inmediatamente. Cada intento conserva la verificación del proceso exacto del propietario, sesión/integridad, autorización nativa y las ocho consultas. Solo Ready/0 permite registrar el host del navegador y continuar. Si tres intentos fallan, se conserva el rollback. No se debilitaron ACL, hashes, roles ni requisitos de salud.

## Pruebas y comandos

- Regresión roja: `DesktopSettingsUiThreadTests` antes de corregir el helper falla por timeout.
- Host: `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj --no-restore --verbosity quiet`: 230/230.
- Sync: última ejecución tras corregir el helper, 139/139.
- `scripts/test-installer.ps1` en Windows PowerShell 5.1: PASS, incluidos ocho casos de readiness, paridad de archivos y lanzamiento real con token de usuario medio. Se corrigió el fixture de lanzamiento para excluir su propio PID: si la suite comenzaba en menos de un segundo, podía confundirse con el proceso de prueba y terminarse a sí misma.
- `FocusGuard-0.1.165-Setup.exe --package-self-test`: exit 0.
- Host del payload .165 `--smoke`, con perfil aislado: exit 0.
- Comparación de payload: Host DLL, host.js, host.css y Service DLL coinciden con .163; el script embebido coincide con el fuente corregido y los Client embebidos coinciden con su compilación corregida.
- Prueba de solo lectura de la instalación restaurada: `FocusGuard.UI.exe --service-health-check`: Ready/0. No se reinstaló ni se inició una sesión de bloqueo.

## Artefactos

| Archivo | Bytes | SHA-256 |
|---|---:|---|
| `artifacts/installer/FocusGuard-0.1.165-Setup.exe` | 371512232 | `9D7C43F9FB01150DC78FE9C118E49D15C10FDD528A299D2A0D0C339C5DC85810` |
| `artifacts/installer/FocusGuard-0.1.165-win-x64.zip` | 552310452 | `9286E685311A7B277EC7466161E600DA97EB2DFFF624D0936B5F55A13F64E3E4` |

Construcción reproducible en `artifacts/blank-window-hotfix/build-readiness-hotfix.ps1`; procedencia `payload-provenance-165.json`, hashes `hashes-165.json` y log `installer-tests-165-final.log` en el mismo directorio.

## Pendiente

Instalar .165 por el propietario y verificar que abre la pantalla y conserva el vínculo. El paquete pasó verificaciones locales; no afirmar aceptación instalada ni que se eliminó toda causa de reinicio del servicio. La Fase 2 permanece abierta por la transferencia física propietario→hermano, duración y preservación de campos locales. Los cambios Host/UI de Fase 3 deben terminar su aceptación separada antes de incluirse en otra versión.
