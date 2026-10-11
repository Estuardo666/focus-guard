# FG-419 — Arranque zen, apertura desde Setup y consumo instalado

## Plan

1. Medir la instalación actual y comprobar que no haya sesiones activas antes de instalar.
2. Reproducir la apertura desde Setup tras el arranque automático en bandeja; revisar lanzamiento a integridad media y activación de instancia existente.
3. Medir y corregir la secuencia visual inicial (tema, fuentes, preferencias, lectura del servicio y módulos diferidos) para presentar una entrada estable y suave.
4. Mantener la suspensión y presupuesto adaptativo FG-418; instalar el candidato, medir visible/oculto/minimizado y validar poca RAM en WebView2 real.
5. Añadir regresiones, ejecutar suites, generar/validar instalador versionado y ejecutar el drill de rollback antes de instalar definitivamente.

## Alcance y seguridad

Autorización explícita del propietario para instalar y probar en la sesión elevada. No hay sesión activa: Expired normal al iniciar. Se preservan datos y configuraciones, reglas, autoridad del servicio, recuperación estricta y accesibilidad. No se automatizan UAC ni credenciales. El nuevo documento de planificación cloud existente queda fuera del alcance.

## Estado

Completado sobre 0.1.148 instalada. Instalador standalone regenerado y validado: `artifacts/installer-fg419/FocusGuard-0.1.148-Setup.exe`, SHA256 `6EAC5AFBE272862B47098EE39595D86084B8518320B79AB16299C3F04A66F96D`.

## Resumen y causas verificadas

- Entrada estable: preparar tema/idioma/movimiento guardados antes de navegar; precargar fuentes y vistas iniciales; presentar la pantalla zen hasta disponer de preferencias, estado y catálogo. Las vistas preparadas se reutilizan, evitando otro frame Suspense. El dashboard empieza con duración y modalidad guardadas.
- Segundo plano: la pérdida de foco pausa el movimiento decorativo conservando el canvas; ocultar/minimizar suspende WebView2 y conserva documento/ediciones. Se mantiene el presupuesto adaptativo FG-418: con menos de 1 GiB libre o 10 % se libera la escena WebGL, con recuperación a 1.5 GiB y 15 %.
- Apertura tras instalar: reproducido sobre .146. El lanzamiento automático usaba ShellExecute SW_HIDE; Windows puede aplicar ese valor a la primera llamada ShowWindow aunque WinForms crea que la ventana está visible. Se usa SW_SHOWNORMAL y `--minimized` decide la bandeja; la presentación explícita verifica la visibilidad nativa. Una prueba con lanzamiento SW_HIDE pasó después del arreglo.
- Setup activa mediante el shell del usuario interactivo, comprueba ruta, sesión, propietario y ventana presentada, espera hasta 5 s y permite reintentar si falla. No anuncia éxito únicamente porque se haya iniciado Explorer. El botón muestra estado de apertura y error localizado.
- Durante las pruebas se encontró otra causa de instancia inaccesible: WM_CLOSE externo destruía el formulario dejando la bandeja viva. Si el formulario realmente se cierra, HostShell termina y limpia sus recursos. Regresión con formulario real y message pump.

## Archivos cambiados

Host: `MainForm.cs`, `Hosting/HostShell.cs`, `Hosting/HostComposition.cs`, `Hosting/WebViewResourceController.cs`, `Program.cs`. Setup: `SetupOperations.cs`, `InstalledAppActivation.cs`, `InteractiveAppLauncher.cs`, `WebSetupForm.cs`, `Program.cs`. Instalación: `scripts/install-focusguard.ps1`. UI: `App.tsx`, `main.tsx`, `DashboardStart.tsx`, `FocusOrb.tsx`, `StartupScreen.tsx`, `startup-presentation.ts`, `index.html`, `design-system/zen.css`, locales y componentes/bridge de Setup. Versionado en package/lock y extensión; script de medición `measure-focusguard-gpu.ps1`; pruebas Host/Setup/React, TASKS y graphify.

## Pruebas añadidas y resultados

- .NET Release: 992 aprobadas, 0 fallidas, 0 omitidas; repetición completa aprobada. Host 201 y Setup 34.
- React: 223/223. Extensión: 19/19. `test-installer.ps1` aprobado.
- WebView2 real: 3/3, con página mínima, UI empaquetada en sesión y UI empaquetada inactiva. Verifican documento y edición sin guardar, suspensión oculta/minimizada, reanudación, canvas liberado/restaurado, controles y reloj. Presión de RAM simulada mediante la lectura de memoria inyectada; no se agota la RAM del propietario.
- Arranque: PerformanceObserver exige CLS <0.05 tras presentar la primera vista. Se corrigió un dato inválido del fixture inactivo (`enforcementConnected=true`); ahora exige estado ready y slider antes de capturar. Las capturas muestran la UI cargada. Evidencia: `artifacts/performance/fg419-visual/`.
- Setup standalone: autoverificación del paquete, smoke React y clásico aprobados. Apertura real inmediatamente tras instalar .148: exit0, misma instancia, ventana presentada. Repetición tras minimizar: exit0, misma instancia, ventana presentada.
- Rollback real ejecutado con candidato .146: fallo intencional tras copiar, `TreeDifferences=0`, servicio Running y Agent activo. UI detenida como requiere el drill; restaurada después y actualización definitiva .148 saludable.

## Comandos ejecutados

`dotnet test FocusGuard.slnx -c Release --no-build --no-restore`; `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~WebViewResourceIntegrationTests`; `npm test -- --maxWorkers=2`; build/check/verify-dist UI; pruebas extensión; `scripts/test-installer.ps1`; formato .NET; `scripts/build-installer.ps1` para .146/.147/.148; standalone `--package-self-test`, `--smoke-web-ui`, `--smoke-test`, `--smoke-open-installed`; Host `--smoke-window-presentation`; `scripts/invoke-installer-rollback-drill.ps1 -Execute -OwnerPresent`; `scripts/install-focusguard.ps1`; scripts de medición de recursos/GPU; `git diff --check`; `graphify update .`.

## Consumo observado y límites de comparación

- .145 antes, ventana visible: 445 MiB privados totales y 5.63 % CPU; GPU media de motores 2.36 %.
- .147 ventana visible sin foco, comprobada: 382 MiB privados totales y 0.052 % CPU; GPU media de motores 0.0034 %. Son instantáneas con distinta actividad/caché, no una garantía de reducción porcentual de RAM.
- .148 minimizada, instancia viva y luego reabierta: árbol UI/WebView2 333 MiB privados / 300 MiB working set; total FocusGuard 418 MiB privados y 0.117 % CPU. Motores GPU 0 % durante cinco muestras; memoria GPU dedicada 42.6 MiB. La suspensión reduce actividad pero retiene memoria para conservar el documento.
- Se descartan como comparación válida las mediciones .146 etiquetadas visible (ventana nativa oculta) y .147 tray (formulario destruido por WM_CLOSE). No se atribuye su caída de memoria a una bandeja saludable.

## UX, seguridad y recuperación

Sin cambios en la autoridad del servicio, reglas ni expiración. Las advertencias estrictas y la recuperación permanecen; se conserva el fallback de Setup y se verifica la identidad del proceso presentado. Preferencias y profiles.db conservan sus hashes. El journal de sesiones es mutable y su hash posterior difiere; no se restaura ni se afirma inmutabilidad. Backend comparado sin cambios en los binarios de reglas/servicio de esta mejora. No hay flags que desactiven GPU globalmente, bucles GC, vaciado forzado de working set ni credenciales/UAC automatizados.

Limitación: el helper nativo de Computer Use no estuvo disponible. Se validaron los mismos métodos de apertura de Setup mediante su smoke y ventanas/procesos reales, sin afirmar una interacción manual completa del instalador. No hay una prueba nueva de presión física extrema ni una auditoría nueva de todas las pantallas/DPI/Narrator. Siguiente tarea pendiente: aceptación FG-410 con el propietario (Edge, reloj real, DPI/accesibilidad y Windows 11); cualquier optimización adicional debe partir de nuevas mediciones.
