# FG-377 — retirar la revisión al vencer

## Objetivo y síntoma

En FG-376 el Host instalado .123 seguía mostrando `Confirmar e iniciar` después de la hora de expiración. A las 13:30, la revisión indicaba «Revisión válida hasta 13:27:27». El servicio seguía conectado y no había sesión activa; no pulsé el botón.

## Root cause hypothesis y confirmación

La revisión normal/ciclo no tenía un temporizador de caducidad en React. `observeStatus` conserva el estado `review` si el servicio informa `Expired` porque ese estado permite preparar una sesión nueva; la vista seguía mostrando el CTA y `confirm` no comprobaba `expiresUtc`. El servicio `InMemoryFocusService` rechaza la preparación si `nowUtc >= ExpiresUtc`, así que no se iniciaba una sesión vencida, pero el Host invitaba al usuario a una acción imposible.

Dos pruebas de reloj falso confirmaron el flujo y fallaban antes del cambio: el panel permanecía visible tras 120 s y `confirm` enviaba `session.startNormal` al llegar exactamente a `expiresUtc`.

## Cambios

- `DashboardStart` comprueba la fecha de expiración cada segundo y al regresar el foco o hacerse visible. Al vencer, invalida la revisión, presenta el mensaje localizado `reviewUnavailable` y devuelve el foco al botón «Revisar sesión».
- `DashboardClient.expireReview` comprueba ID, fase y fecha antes de retirar la revisión. `confirm` vuelve a comprobar `expiresUtc` inmediatamente antes de enviar, para cerrar la carrera entre el temporizador y un clic.
- No cambia el servicio, IPC ni política. No hay animación nueva; la transición es inmediata y accesible.
- `package.json`, `package-lock.json` y la extensión quedan en .129. El Setup/ZIP candidato se conserva en `D:\TEMP\FocusGuard-FG377-20261009\`.

## Archivos cambiados

- `src/FocusGuard.UI/src/DashboardStart.tsx`
- `src/FocusGuard.UI/src/native/dashboard.ts`
- `src/FocusGuard.UI/src/DashboardStart.test.tsx` (dos regresiones con reloj falso)
- `src/FocusGuard.UI/package.json`, `src/FocusGuard.UI/package-lock.json` y `extension/manifest.json` (versión .129)
- `TASKS.md` y `docs/FOCUSGUARD_UI_MIGRATION_PLAN.md`
- `graphify-out/GRAPH_REPORT.md`, `graphify-out/graph.json` y `graphify-out/ast-extraction.json`

## Implicaciones UX y de seguridad

Al vencer, el mensaje localizado aparece como alerta, el borrador deja de ser confirmable y el foco vuelve al control para preparar otra revisión. No se añadió animación. La defensa del servicio contra una preparación expirada sigue siendo la autoridad; la nueva comprobación de React evita enviar un startNormal/startCycle ya vencido. No se modificaron políticas, ACL ni IPC.

## Verificación

- Las dos pruebas de caducidad nuevas pasan; suite React: 23 archivos, 144/144.
- `npm run build` pasa TypeScript, Vite, CSP, contraste, layout mínimo, forced-colors, teclado y reduced-motion.
- `test-installer.ps1` pasa. `test-browser-extension-package.ps1 -Version 0.1.129` pasa para Chrome y Edge.
- `dotnet test FocusGuard.slnx`: 889/889 en configuración Debug.
- La misma solución en Release dio un fallo ajeno a FG-377: `ColdIdentityProbe` se compiló en `bin\Debug` mientras el test Release buscó `bin\Release`. Ejecutar `FocusGuard.Ipc.Windows.Tests.csproj` directamente en Release produjo 79/79; queda esa discrepancia de configuración de la solución como seguimiento separado.
- Setup .129: manifiesto 31/31, Setup standalone y `FocusGuard.Setup.exe` del paquete tienen el mismo SHA-256. SHA-256 Setup `011580839125035BC4B7541CC583D4957F972E0CF17CC3A0026DD9B8238788A8`; ZIP `71B5F1F6122472F5F5D867E1E8F32FC3285EC354E0D3AA892CC24CC9E6F774E6`.
- El `DashboardStart.js` empaquetado coincide byte por byte con `src/FocusGuard.UI/dist` (SHA `F08F1214EEFF3230A5BF74E3516E90C2576012DB86CF40790A4C7D0073D2981B`); contiene `expireReview` y el bundle anfitrión `PreparationUnavailable`.
- `invoke-installer-rollback-drill.ps1 -PreflightOnly` validó .129 frente a .123: 31 archivos, Service Running como LocalService, sesión Expired, Strict false, consentimiento presente. `InstallerInvoked=false`, `MachineStateChanged=false`.

Comandos principales: `npm test`; `npm run build`; `dotnet test FocusGuard.slnx`; `dotnet test FocusGuard.slnx --configuration Release`; `dotnet test tests\FocusGuard.Ipc.Windows.Tests\FocusGuard.Ipc.Windows.Tests.csproj --configuration Release`; `scripts\build-installer.ps1 -Version 0.1.129` con salida en D; `scripts\test-installer.ps1`; `scripts\test-browser-extension-package.ps1 -Version 0.1.129`; `scripts\invoke-installer-rollback-drill.ps1 -PackageDirectory D:\TEMP\FocusGuard-FG377-20261009\package -PreflightOnly`; `graphify update .`.

## Recuperación de espacio

La compilación y las pruebas repoblaron cachés e intermediarios bajo E. Los moví después de validar el paquete a `D:\TEMP\FocusGuard-FG377-build-archive-20261009\`. El manifiesto registra 69 directorios y 8,293 archivos, con igualdad de número de archivos y bytes en cada destino. E quedó con 135.83 GiB libres; D, con 41.24 GiB. `src\FocusGuard.UI\node_modules`, fuente y el paquete final .129 permanecen en sus rutas activas.

## Limitaciones y siguiente paso

.123 seguía instalado al cerrar la etapa de empaquetado .129; después, FG-377 se aceptó en el Host real y FG-378 instaló .130 con la alerta visible. Los gates que siguen pendientes son los demás recorridos de .130, Edge real, la matriz instalada 1024×700/800×600/720×480, Narrator, forced-colors, cambio real de DPI, Windows 11 y carga/recovery.

La PowerShell administradora PID29908 permaneció fuera de pantalla. La instalación se completó con `install-focusguard.ps1` desde una nueva PowerShell elevada vía RunAs; exit code 0 y comprobación IPC correcta. El Host se ejecutó de nuevo como usuario normal.

Siguiente paso: instalar el candidato .129 en el Host real y aceptar la revisión con un vencimiento cercano, sin iniciar ninguna sesión expirada.

## Informe de depuración

**Síntoma:** revisión normal vencida seguía en pantalla con CTA habilitado.

**Causa raíz:** faltaba invalidar el estado `review` por `expiresUtc`; el estado terminal `Expired` permitía iniciar otra sesión y no borraba el borrador.

**Arreglo:** expiración local con mensaje y foco; verificación síncrona en el cliente antes de enviar startNormal/startCycle.

**Regresión:** dos pruebas con reloj falso que fallaban antes; pasan ahora.

**Estado:** DONE: la caducidad se corrigió, el paquete .129 pasó la aceptación instalada y .130 verificó además que el aviso sea visible sin desplazar la página.
