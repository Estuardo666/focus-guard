# FG-287 — Setup no debe matar el Service durante el preflight

## DEBUG REPORT

**Síntoma.** Al actualizar con Setup .96/.97, Windows registró SCM 7034 y luego el Service reapareció con otro PID. El preflight listaba `FocusGuard Service` junto con las aplicaciones abiertas y anotaba que lo estaba cerrando.

**Causa raíz.** En [Program.cs](../../src/FocusGuard.Setup/Program.cs), `SetupForm.CloseProcess` intentaba `CloseMainWindow()` y esperaba tres segundos; al no haber una ventana que cerrar en el proceso de servicio, acababa ejecutando `Process.Kill(entireProcessTree:true)`. Eso terminaba el Service fuera del SCM antes de que `install-focusguard.ps1` llegara a su `Stop-Service`. El evento 7034 de15:04:46 siguió al preflight de .96 que cerró Service PID9308; el de15:25:11 siguió al preflight de .97. `service-failures.jsonl` no contenía entradas del7-oct y el rechazo IPC copiado era antiguo; el Application log no mostró una excepción del Service en esas ventanas.

Durante la misma investigación confirmé un segundo ciclo de cierre: Chrome/Edge reconectaban `FocusGuard.NativeMessaging` mientras su registro seguía activo. El script de instalación ya suspende ese registro antes de detener el host, pero el preflight interactivo lo mataba antes y volvía a encontrarlo en sus reintentos.

**Cambio.** `SetupForm.GetProcessesToClose` ahora saca de la lista de terminación forzada el Service y Native Messaging. Setup explica que el script de instalación los detendrá de forma ordenada. El modal sigue mostrando y pidiendo confirmación para Desktop/Agent; `install-focusguard.ps1` suspende el registro, cierra NM y usa `Stop-Service` antes de reemplazar archivos.

**Regresión.** `--smoke-restart-manager-filter` prueba un Service solo, Native Messaging solo y una mezcla con Desktop. Antes de filtrar, el smoke devolvió1; al filtrar sólo el Service, falló de nuevo para Native Messaging; con ambos filtros devuelve0. También pasan `scripts/test-installer.ps1`, el build/contraste/CSP, los tests de extensión19/19 y `dotnet test FocusGuard.slnx -c Release --no-restore`.

**Verificación instalada.** Setup .98 está instalado. El preflight real mostró únicamente Desktop y ProcessAgent; Service y Native Messaging aparecieron en la explicación de que la transacción los manejaría. La instalación terminó. Los hashes de 18/18 recursos UI y 7/7 ejecutables coinciden; los package-self-test, Setup UI smoke, filtro de preflight smoke y cuatro smokes Host terminan0. DrillClient da Ready/extensionReady/Inactive; Service PID5816 corre como LocalService. El total de eventos SCM7034 quedó en70, igual a la línea base; el último sigue siendo15:25:11, antes de la corrección. Host39536, Desktop34960 y Agent26892 permanecieron estables durante los smokes. No inicié ni detuve sesiones ni modifiqué perfiles.

**Prueba visual.** `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/setup-preflight-no-service-or-native-host-0.1.98.png` muestra la lista del preflight sin Service ni Native Messaging. `installed-profile-after-fixed-setup-0.1.98.png` muestra la UI restaurada.

**Evidencia.** `build-receipt-fixed-0.1.98.json`, `installed-acceptance-fixed-0.1.98.json`, `scm7034-baseline-before-fixed-setup-0.1.98.json` y `scm7034-after-fixed-setup-0.1.98.json` están en `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/`.

**Relacionado.** Cierra el gate instalado de Fase4 para muerte inesperada durante Setup y el failpoint de rollback FG-285. El System log no tenía una excepción 7034 nueva después de la corrección. Continúan pendientes continuidad del Host con sesión activa, Activating real, forced-colors/Narrator/DPI, Edge, carga sostenida/reboot/recovery y equivalencia final de Fases5–10.

**Estado de esta investigación:** DONE. La migración completa sigue en curso.
