# Fase 2 — auditoría de evidencia y cierre del ensayo Windows

## Estado final

La aceptación física principal está completada. El propietario confirma ambas direcciones de transferencia: hermano→propietario para tema/idioma y propietario→hermano para tema/idioma/duración. La importación en la PC del hermano acredita que el segundo Windows abrió y descifró el documento. El propietario también confirma que la sesión de cuenta y los ajustes se conservaron al reiniciar y cerrar/reabrir FocusGuard.

Reglas, perfiles y permisos de la PC del hermano no se resetearon. Son datos locales y no se sincronizan entre PCs en esta versión; su conservación durante la actualización/importación no implica que hayan sido transferidos. No se reportó que hubiera una sesión activa de enfoque durante la importación.

La vinculación Android sí se acepta por las fotos del Samsung del 13 de octubre: la misma huella aparece enlazada, la membresía se muestra verificada y Windows no tiene solicitudes pendientes. La alpha.3 corrige un duplicado cosmético y pasó 8/8 pruebas instrumentadas más lint en AVD Android 15/API 35; no se afirma que alpha.3 se haya instalado en el Samsung ni que Android aplique bloqueo.

## Estado de disponibilidad y preparación

Actualización física posterior: el propietario instaló .166 en la PC de su hermano. La primera captura post-update muestra la cuenta Not connected, servicio Connected, extensión Disconnected y ninguna sesión activa. Tras login, otra captura muestra email verificado, dispositivo vinculado, privacidad E2EE y ninguna solicitud pendiente. Esto acepta la continuidad del vínculo tras login; no demuestra que Setup haya causado el estado inicial sin sesión porque no hay captura pre-update, ni demuestra descifrado propietario→hermano. El teléfono Android sigue sin estar disponible y este shell no expone `adb`/Java/Android SDK para una nueva prueba AVD.

Reinicio físico completado en la PC del hermano: la foto del 14 de octubre posterior al reinicio muestra Signed in, Email Verified, Device Linked, End-to-end encrypted y ninguna solicitud pendiente. El propietario confirma además que al abrir y cerrar FocusGuard la sesión de cuenta y los ajustes seguían guardados. La captura no muestra el estado de servicio/extensión, y no hay hash del binario de esa PC.

Transferencia propietario→hermano realizada: el propietario confirma que tema, idioma y duración se exportaron/importaron correctamente. Esto acepta el descifrado físico de las tres opciones en el segundo Windows. No se adjuntó el preview ni hash del binario del hermano; la persistencia y conservación local posteriores sí quedaron confirmadas por reporte directo.

El instalador .160 fue el candidato del primer checkpoint. Después, Host conservó y validó la historia firmada de membresía al leer documentos antiguos; el candidato .161 se compiló y pasó suite Host 222/222, `scripts/test-installer.ps1` y `--package-self-test`, SHA-256 `5E7B16DE45070B09926D6CB02605E3784D96E817A53126D6D95A6A4200C1C508`. No se instaló ni se desplegó.

Las pruebas sintéticas del fixture inicial (124 comprobaciones) cubrían aislamiento de cuenta, replay, revocación y E2EE, pero no sustituían el paso físico de descifrado en la segunda PC. La revalidación integrada posterior del 14 de octubre pasó 125 checks con Neon TLS y datos de cuenta exclusivamente sintéticos; no se usaron credenciales personales ni se desplegó API.

Prueba local dirigida del 14 de octubre: `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --no-build --nologo -m:1 --filter "FullyQualifiedName~SharedDesktopSettingsTests|FullyQualifiedName~SettingsTransferBridgeAcceptsNoWebSuppliedValuesAndReturnsOnlyStatus"` pasó 4/4. Cubre allowlist/merge de selección, preservar campos locales, roundtrip del documento cifrado, rechazo de otro autor y puente que no acepta valores elegidos por JavaScript. Es evidencia de código local, no prueba física de dos Windows.

Continuación local realizada: `SharedDesktopSettingsTests.MergeAndImportOnlySelectedAllowlistedPreferences` ahora inicializa propiedades locales no compartidas (presentación al terminar sesión, reducción de movimiento, temas por perfil, enforcement, notificaciones de ciclo, perfil/preset seleccionados, ventana compacta y modo de sesión) y comprueba que se conservan al importar tema/idioma/duración. La suite enfocada `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --filter "FullyQualifiedName~SharedDesktopSettingsTests" --no-restore` pasó 3/3. Esto aporta evidencia de código para las preferencias `DesktopSettings`; el reporte posterior del propietario confirma por separado que reglas, perfiles y permisos no se resetearon. No se probó una sesión activa de enfoque durante la importación.

Verificación durable realizada: `SharedDesktopSettingsTests.PersistingImportedSettingsPreservesConcurrentUnselectedLocalChanges` crea dos `DesktopSettingsStore` sobre el mismo archivo temporal. Mientras el importador mantiene su snapshot, el segundo almacén cambia preferencias locales; al guardar, la importación aplica tema/idioma/duración y conserva esos cambios concurrentes. La suite enfocada ahora pasa 4/4 con `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --filter "FullyQualifiedName~SharedDesktopSettingsTests" --no-restore`. El test usa ruta temporal propia y no lee/escribe las preferencias instaladas.

`graphify update .` terminó con código 0 después del cambio de persistencia: AST 1.976/1.976 archivos; grafo de código actualizado a 23.544 nodos, 152.114 aristas y 1.930 comunidades. La visualización HTML se omitió por superar su límite de nodos; el grafo JSON y el informe sí se regeneraron.

Preflight público y de sólo lectura del 14 de octubre: API `/health/ready` HTTP 200 con `{"status":"ready"}`; OIDC discovery HTTP 200 con issuer exacto `https://auth.focusguard.totemmassmedia.com/realms/focusguard`. No se inició sesión, no se consultó ni modificó una cuenta; esto no equivale a pairing ni transferencia.

Revisión del flujo de cancelación: `ShareSelectedSettingsAsync` registra `Cancelled` y retorna antes de leer el documento compartido o emitir el POST. El ensayo físico añadido a la guía comprobará que los valores del otro PC y los locales sigan intactos tras cancelar.

## Cierre y pendientes complementarios

La aceptación principal de Fase 2 está completada: el segundo Windows descifró e importó las tres opciones, los ajustes y la sesión de cuenta persistieron tras reiniciar/cerrar/reabrir, y el propietario confirma que los datos locales del hermano permanecieron intactos. No hay hash del binario del hermano ni captura del preview. La prueba física de aislamiento de otra cuenta, replay, revocación y contraseña independiente se complementa con el fixture sintético actualizado (125 comprobaciones); no se repitió con credenciales personales.

Como smoke opcionales quedan observar el estado de la extensión tras login, importar un único ajuste en sentido inverso, cancelar una transferencia y probar un Simple Timer normal. No bloquean el cierre de la aceptación principal. Reglas, perfiles y permisos no se sincronizan entre PCs en esta versión.

Seguimiento de limpieza posterior: la carpeta y los dos paquetes `.164` previamente enlazados ya no aparecen en `artifacts/installer`; `.165` y `.166` permanecen. El intento del agente había sido bloqueado por política y no hubo otro intento; no se infiere quién retiró los archivos. D: aumentó 1.878.994.944 bytes entre mediciones, valor compatible con la limpieza pero sin atribución exacta. Espacio actual: C: 14,35 GiB, D: 107,12 GiB, E: 78,23 GiB. Las notas históricas de volcados y `.160`–`.163` requieren otro inventario antes de actuar. Detalle: `docs/progress/20261014-installer-artifact-inventory.md`.
