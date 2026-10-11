# FG-415 — Fase 10, paso 1: React sustituye a Avalonia (instalador incluido)

Fecha: 2026-10-10. Decisión del propietario (2026-10-10): retirada **en dos pasos**. Paso 1 (este): React hace todo lo que hacía Avalonia y el instalador deja de instalar Avalonia; el código Avalonia sigue en el repositorio, compilando y con tests, como respaldo. Paso 2 (más adelante, cuando el propietario confirme que lo usa sin problemas): borrar `FocusGuard.Desktop` y sus tests. Avisos: «Popup FocusGuard» (ventana React pequeña, funciona con la ventana principal cerrada). Bandeja: «Panel React».

## Resumen

`FocusGuard.UI.exe` (Host WebView2) pasa a ser la única interfaz instalada:

- **Bandeja** (`HostShell`, `ApplicationContext`): icono de notificación siempre presente; clic izquierdo abre un panel React (360×480, esquina inferior derecha del área de trabajo) con la sesión en curso (tiempo restante en vivo, perfil, sitios/apps, hora de fin), «Abrir», «Vista compacta», «Preferencias» y «Salir» («La sesión sigue activa aunque salgas»). Menú contextual nativo con las mismas acciones. Tooltip «quedan N min».
- **Cerrar la ventana** la oculta en la bandeja (como Avalonia); «Salir» cierra todo. La sesión vive en el servicio en ambos casos.
- **Avisos** (popup sin robar foco, 380×112): fin de sesión (según la preferencia «Al terminar»: aviso, ventana, ventana con fondo atenuado o nada; nunca duplica el resumen si la ventana principal está a la vista) y cambio de fase de ciclo (si la preferencia está activa y la ventana no está a la vista). Se cierran solos a los 9 s salvo con el ratón encima.
- **Resumen de sesión** como ventana propia (460×560) con el mismo detalle que Actividad; modo atenuado con velos en todos los monitores.
- **Pantalla de bloqueo** al abrir una app bloqueada: orbe con cuenta atrás, «{app} está bloqueada», hora de fin, nota de modo estricto y «Volver al trabajo». Superficie interactiva en el monitor principal y velo 0,86 en los demás. Esc/Alt+F4 no la cierran; **la tecla Windows no se intercepta** (AGENTS.md: no bloquear herramientas de recuperación). Se cierra sola al terminar la sesión. Ignora el historial ya existente en la primera lectura.
- **Inicio con Windows**: interruptor nuevo en Preferencias › Sesiones («Abrir al iniciar sesión»), vía `app.startup.get/set` sólo en la ventana principal; escribe únicamente el valor `Run` del usuario (mismo nombre histórico `FocusGuard.Desktop`, ahora `"…\FocusGuard.UI.exe" --minimized`), así la elección previa se conserva al actualizar.
- **Instancia única** por usuario y sesión (`DesktopSingleInstance`): un segundo arranque trae la existente al frente. Un arranque elevado (botón «Abrir FocusGuard» del Setup) ya no muestra el error de elevación si hay una instancia en marcha: sólo la señala (`TrySignalExisting`, nunca crea el mutex).
- **Health probe del instalador**: `FocusGuard.UI.exe --service-health-check --installer-owner-probe` (antes Desktop), mismos códigos 0/11/12/14 y diagnóstico en `%LOCALAPPDATA%\FocusGuard\diagnostics\health-check.json`.
- El ejecutable lleva el icono de FocusGuard (bandeja, ventana, acceso directo).

### Seguridad del bridge

- Cada popup carga la misma UI empaquetada con el mismo endurecimiento WebView2 que la ventana principal (`WebViewHardening.Apply`: sólo `https://focusguard.app`, CSP, sin navegación externa, descargas, permisos ni DevTools) y su propio `DocumentLifetime`/token.
- `host.surface` se valida campo a campo en la página (claves exactas, GUID, fechas UTC `Z`, longitudes); el host serializa las fechas siempre en UTC.
- Comandos nuevos con parser estricto: `app.open {section}` (lista cerrada de secciones) o `{section:"activity", sessionId}`, `app.compact {}`, `app.quit {}`, `surface.close {}`, `app.startup.get {}`, `app.startup.set {enabled:bool}`. Cualquier parámetro extra o desconocido → `MethodOrParametersRejected` antes de llegar al shell. Sin shell/registro → `WindowUnavailable`.
- Política IPC: `WebViewHost` (`FocusGuard.UI.exe/.dll`) pasa a rol **obligatorio**; `Desktop` deja de declararse (no se instala ni se confía). El servicio ya trataba ambos roles igual.

### Instalador

- `build-installer.ps1` deja de publicar `FocusGuard.Desktop`; la comprobación anti-payload-obsoleto usa ahora `FocusGuard.UI.dll`.
- `install-focusguard.ps1`: acceso directo del menú Inicio y `Run` → `FocusGuard.UI.exe`; arranque final `FocusGuard.UI.exe --minimized`; health probe por el Host. Para actualizar desde versiones con Avalonia: sigue cerrando procesos `FocusGuard.Desktop` y borra sólo `FocusGuard.Desktop.*` del directorio instalado (la copia de seguridad previa los conserva para rollback). El Host en bandeja se cierra con `CloseMainWindow` y, si sigue vivo a los 2 s, `Stop-Process` (como hacía con Desktop).
- Setup: «Abrir FocusGuard» lanza `FocusGuard.UI.exe`; paquete embebido sin Desktop.
- `invoke-installer-rollback-drill.ps1` busca el proceso `FocusGuard.UI`.

## Archivos

Host: `Hosting/HostShell.cs`, `SurfaceForm.cs`, `Hosting/SessionEventMonitor.cs`, `Hosting/ServiceHealthProbe.cs` (nuevos); `Program.cs`, `MainForm.cs`, `Hosting/HostComposition.cs`, `Hosting/IHostWindowController.cs`, `Bridge/BridgeContracts.cs`, `Bridge/BridgeRequestParser.cs`, `Bridge/NativeBridgeDispatcher.cs`, `Hosting/WebViewSecurityPolicy.cs`, `FocusGuard.Host.Windows.csproj`, `Resources/HostStrings*.resx`.
Plataforma: `FocusGuard.Platform.Windows/DesktopSingleInstance.cs` (`TrySignalExisting`).
React: `native/shell.ts`, `Surfaces.tsx` (nuevos); `main.tsx`, `App.tsx`, `PreferencesPanel.tsx`, `zen.css`, `locales/es.json`, `locales/en.json`, `dev/preview-host.ts`, `scripts/verify-dist.mjs`.
Instalador: `scripts/build-installer.ps1`, `scripts/install-focusguard.ps1`, `scripts/New-FocusGuardIpcPolicy.ps1`, `scripts/test-installer.ps1`, `scripts/test-ipc-policy-provisioning.ps1`, `scripts/invoke-installer-rollback-drill.ps1`, `src/FocusGuard.Setup/EmbeddedPackage.cs`, `src/FocusGuard.Setup/Program.cs`. Versión 0.1.141 (`extension/manifest.json`, `package.json`).

## Pruebas añadidas

- `tests/FocusGuard.Host.Windows.Tests/HostShellTests.cs`: cierre de sesión una sola vez y nunca para una sesión no vista; transiciones de fase sin repetir; bloqueo de app ignora el historial inicial y eventos web; monitor emite fin y bloqueo y respeta el intervalo de actividad; no lee auditoría sin enforcement; comandos del shell llegan al controlador; comandos mal formados se rechazan antes; sin shell → `WindowUnavailable`; inicio con Windows leído/escrito sólo con registro (popup → `WindowUnavailable`, tipo erróneo rechazado).
- `native/shell.test.ts`: validación estricta de superficies (claves extra, GUID, fechas no UTC, versión); modo principal vs popup por token; navegación sólo del documento actual y secciones conocidas; comandos ligados al token; respuesta de inicio emparejada por requestId.
- `Surfaces.test.tsx`: panel de bandeja (acciones y aviso de que la sesión sigue), bandeja en reposo, aviso de fase `role=status` que se cierra solo, pantalla de bloqueo (nombre, tiempo, estricto, volver) y cierre automático al acabar.
- `PreferencesPanel.test.tsx`: interruptor de inicio sólo cuando el host puede leerlo y refleja lo guardado.
- `test-ipc-policy-provisioning.ps1` y `test-installer.ps1` actualizados (roles, Desktop retirado del build/instalador, orden del smoke DPI).

## Comandos y resultados

- `dotnet test FocusGuard.slnx`: 938/938 (Host 181, Service 138, Client 112, Ipc 79, Desktop 189 + UiTests 15 —Avalonia sigue compilando—, resto sin cambios).
- `npx tsc -b` OK; `npx vitest run`: 207/207 (31 archivos). `npm run build` + `verify-dist` OK (chunk `focus-time.js` ya no se emite por separado; lista permitida actualizada en los 5 sitios).
- `scripts/test-installer.ps1`: OK. `scripts/test-ipc-policy-provisioning.ps1`: todas las aserciones de roles pasan; el paso de junction no puede ejecutarse en `E:` (exFAT, limitación previa del entorno).
- `scripts/build-installer.ps1 -Version 0.1.141 -OutputDirectory artifacts/installer-r12/FocusGuard-0.1.141`: `artifacts/installer-r12/FocusGuard-0.1.141-Setup.exe` 364,341,432 bytes, SHA-256 `CAE2F29FF8B6C24D292B1DCB75CA9B4A68737491783D0E3FD83AA67AD9A04465`; zip 542,392,658 bytes. `--package-self-test` 0; ningún `FocusGuard.Desktop*` en el paquete (antes 443 MB).
- Vista previa del navegador: bandeja, aviso de fin, aviso de fase, resumen y bloqueo renderizan; la app principal sigue igual.
- `FocusGuard.UI.exe --smoke-window-dpi` devuelve 4 en esta pantalla **también con r11 y con la versión instalada**: condición previa del monitor actual, no regresión.

## UX

Se mantiene el comportamiento conocido de Avalonia (cerrar = bandeja, avisos, resumen, bloqueo) con el estilo zen. El idioma de los popups sigue la preferencia guardada. Bloqueo y avisos respetan movimiento reducido. Las sesiones estrictas siguen mostrando consecuencias y hora de fin exacta.

## Seguridad

Sin nuevos privilegios: el Host sigue negándose a correr elevado; el instalador no cambia ACL ni servicio respecto a .140 salvo la política IPC (WebViewHost obligatorio, Desktop fuera). La pantalla de bloqueo no secuestra la tecla Windows ni bloquea herramientas de recuperación. El único estado de máquina nuevo es el valor `Run` del propio usuario, ya existente.

## Limitaciones

- **No instalado**: la sesión de Claude no está elevada; el propietario debe ejecutar el Setup (doble clic + UAC). No instalar con una sesión Active/Activating/RecoveryPending (`C:\Program Files\FocusGuard\FocusGuard.DrillClient.exe status`).
- Pendiente verificar en la app instalada: icono de bandeja y panel, aviso con la ventana cerrada, pantalla de bloqueo con una app real, inicio con Windows, «Abrir FocusGuard» del Setup trayendo la instancia al frente, e iconos de aplicaciones.
- Restos de dependencias de Avalonia (`Avalonia*.dll`, Skia…) pueden quedar en el directorio instalado al actualizar: son inertes (sin punto de entrada) y desaparecen con una instalación limpia; se retirarán en el paso 2.
- La frase de confirmación de dos palabras de la UI clásica deja de estar disponible porque la UI clásica ya no se instala (SPEC ya usa el nombre exacto del perfil, FG-413).

## Siguiente

Instalación por el propietario y verificación en la app real; tras su confirmación de uso sin problemas, **paso 2**: borrar `src/FocusGuard.Desktop`, `tests/FocusGuard.Desktop*` y las referencias restantes.

## Corrección tras instalar .141 (r12)

El propietario instaló r12 y «no se abría nada». Diagnóstico de solo lectura: servicio Ready/Inactive, Host `FocusGuard.UI` en marcha en la bandeja, pero con un diálogo modal «Microsoft .NET» (excepción no controlada) que bloqueaba el hilo de la UI, así que la segunda apertura no podía traer la ventana. Causa: WinForms lee `CreateParams` desde el constructor base de `Form`, antes de que `SurfaceForm` asigne `_spec` → `NullReferenceException` al pulsar el icono de la bandeja.

- `SurfaceForm`: `CreateParams`/`ShowWithoutActivation` toleran el spec aún sin asignar (los valores reales se aplican al crear el handle).
- `HostShell`: los popups ya no pueden dejar un diálogo modal bloqueando la app; el fallo se registra en `%LOCALAPPDATA%\FocusGuard\diagnostics\host-shell-errors.log` (máx. 64 KiB, sin datos de usuario) y la bandeja sigue funcionando.
- El panel de la bandeja se abre junto a la barra de tareas también cuando está arriba o a la izquierda (la del propietario está arriba).
- Prueba nueva `Every_popup_kind_can_be_constructed_and_only_notices_skip_activation` (4 casos): falla con `NullReferenceException` sin el arreglo y pasa con él. Host 185/185.
- Instalador regenerado en `artifacts/installer-r13/`: `FocusGuard-0.1.141-Setup.exe` 364,345,528 bytes, SHA-256 `3FE65A0174E263F03F4DF1FF1887B97755AF093F222B8AB0132E5E71520ABE2B`; `--package-self-test` 0; `test-installer.ps1` OK.
