# FG-418 — RAM y GPU adaptativas

## Plan

1. Confirmar los costes del orbe WebGL, fondos animados y ciclo de vida de WebView2.
2. Suspender el renderer al ocultar/minimizar, preservando documento y formularios; reanudar al volver.
3. Detectar poca RAM disponible con histéresis y sustituir solo el adorno WebGL por su respaldo CSS, preservando temporizador, controles, avisos y servicio.
4. Reducir geometría y frecuencia de dibujo decorativo; liberar contextos GPU al desmontar.
5. Probar presión de memoria, carreras ocultar/mostrar, conservación del estado y suite completa; compilar y validar instalador independiente versionado.

## Implicaciones

- UX: mismo diseño y controles; solo los adornos se simplifican temporalmente bajo presión.
- Seguridad: no cambiar IPC, autoridad del servicio, políticas ni recuperación estricta.
- Recuperación: un fallo del ajuste de recursos no debe impedir abrir o recuperar la UI.

## Resultado

Implementado y empaquetado en 0.1.145. Código localizado en E:; terminal sin elevación. La instalación activa no se modifica.

### Causa y cambios

- El orbe tenía una esfera de 160×160 segmentos (50.880 triángulos por capa), una prueba de soporte que creaba otro contexto GPU y un bucle requestAnimationFrame perpetuo, incluso con movimiento reducido. Ahora usa 64×64 (8.064 triángulos, −84,15 %), ratio de píxel máximo 1,5 en lugar de 2 y un máximo de 30 dibujos/s. Conserva los shaders, estilos, colores, seguimiento, respiración y controles DOM. No quedan frames/timers decorativos con movimiento reducido o escena oculta. Al desmontar libera geometría, materiales, renderer y contexto WebGL.
- `WebViewResourceController` muestrea RAM física disponible cada 5 s: modo ligero por debajo de 1 GiB **o** 10 %; recuperación solo con al menos 1,5 GiB **y** 15 %. Las muestras inválidas conservan el modo anterior. El modo ligero desmonta solo la escena WebGL, usa su respaldo CSS con la paleta y deja el fondo como gradientes estáticos; no desactiva la GPU de toda la aplicación ni cambia preferencias persistidas.
- Al ocultar o minimizar la ventana se sincroniza la visibilidad nativa y se llama TrySuspendAsync; al mostrarla se reanuda. Se conservan documento y ediciones sin guardar. Una suspensión que termina después de reabrir vuelve a reanudar; no se duplican solicitudes pendientes ni se toca un control tras desecharlo.
- La prueba real detectó que WinForms podía omitir la notificación del hijo al ocultar su padre: el getter de Visible ya era false, pero el controller nativo seguía visible y TrySuspend fallaba (mensaje SDK engañoso «disposed», estado inválido). `ResourceWebView2` sincroniza mediante el método protegido OnVisibleChanged del SDK; ambas pruebas reales confirman IsSuspended en ocultación y minimización.
- La cuenta atrás dibuja una vez por segundo, alineada al dato del servicio, deja de programar actualizaciones mientras el documento está oculto y recupera el tiempo transcurrido con performance.now al volver. El servicio sigue siendo la autoridad.

La suspensión sigue la [API de WebView2](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.trysuspendasync). No se mezcla con MemoryUsageTargetLevel, combinación desaconsejada por Microsoft.

### Archivos

- Host: `MainForm.cs`, `SurfaceForm.cs`, nuevo `Hosting/WebViewResourceController.cs`.
- React: `FocusOrb.tsx`, `FocusOrbScene.tsx`, nuevo `decorative-frame-loop.ts`, `live-remaining.ts`, `design-system/zen.css`.
- Versiones: `src/FocusGuard.UI/package.json`, lockfile y `extension/manifest.json` → 0.1.145.
- Pruebas: nuevos `ResourceBudgetTests.cs`, `WebViewResourceIntegrationTests.cs`, `FocusOrb.resources.test.tsx`, `decorative-frame-loop.test.ts`, `live-remaining.test.tsx`.
- Seguimiento: `TASKS.md`, este checkpoint y graphify actualizado con AST local.

### Pruebas y comandos

- Base React antes de editar: 215/215. Final React: 220/220 (`npm test -- --maxWorkers=2`), sin errores no controlados.
- .NET Release completo: **986/986**, 0 fallos/0 omitidas (`dotnet test FocusGuard.slnx --configuration Release`, TRX en `artifacts/test-results/fg418-final`). Incluye Domain, Service, Client, IPC, almacenamiento, recuperación, Host, Desktop y Setup.
- 10 pruebas nuevas de presupuesto/presión/carreras y 2 de WebView2 real: presión inyectada de 512 MiB disponibles, recuperación a 4 GiB, contenido y objeto JS conservados, suspensión al ocultar/minimizar, timers reanudados, escena GPU retirada/restaurada y estado fresco tras regresar. No se agota la RAM del sistema. La variante con UI usa los mismos assets de producción.
- La primera corrida Release reveló un problema previo del probe ColdIdentity: la solución construye ese proyecto referenciado en Debug y el target de copia Release no tenía su DLL. `dotnet build tests/FocusGuard.Ipc.ColdIdentityProbe/FocusGuard.Ipc.ColdIdentityProbe.csproj --configuration Release` preparó el artefacto; la suite completa final pasa sin cambiar IPC ni su prueba.
- Las corridas enfocadas con BuildProjectReferences=false no copian contenido transitivo. La prueba nativa comprueba index.html y localiza dist ya construido cuando falta la copia; fallos diagnósticos distinguen ERR_FILE_NOT_FOUND de errores del producto. La corrida enfocada final pasa 2/2, incluida la confirmación de cabina tras una lectura fresca.
- Extensión: 19/19; `scripts/test-installer.ps1`: OK; TypeScript, build/verify-dist (CSP, contraste, accesibilidad, movimientos/transparencias reducidas): OK; dotnet format de archivos C# nuevos y git diff --check.
- Host **publicado**: `--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure`, `--smoke-renderer-crash`, `--smoke-window-dpi`: todos exit 0.
- Instalador: `scripts/build-installer.ps1 -Version 0.1.145`, verificación de hashes del payload embebido, `--package-self-test`, `--smoke-web-ui`, `--smoke-test`: todos exit 0. El primer empaquetado rechazó la versión anterior del manifest de extensión; se actualizó a .145 y se regeneró el paquete completo.
- `graphify update .`: correcto, sin LLM. El grafo grande no genera HTML, como antes.

### Evidencia y paquete

- Instalador: `artifacts/installer-fg418/FocusGuard-0.1.145-Setup.exe`, 366.400.424 bytes.
- SHA-256: `6463C584F69FD633524CDDF9333A1D2FC7EC639EF876DF27987667F8C4254FD4`.
- Resumen reproducible: `artifacts/performance/fg418-validation.json`; smokes Host y Setup en esa carpeta.
- Capturas inspeccionadas: `artifacts/performance/fg418-visual/{normal,low-memory,recovered}.png`. Conservan texto, temporizador y acciones; el respaldo ligero simplifica únicamente el adorno.
- Medición de la versión **instalada anterior**, no de .145: `fg418-installed-before.json`, muestra de 5 s: árbol UI+WebView2 ~643 MiB de working set sumado, UI+compañeros ~827 MiB; servicio dentro del presupuesto. No confundir working sets sumados (pueden incluir memoria compartida) con RAM privada ni GPU dedicada. No existe una comparación instalada antes/después en esta sesión.

### UX y seguridad

No cambia la autoridad del servicio, las reglas, los plazos, IPC, autorización, la recuperación estricta, los datos ni las funciones. El respaldo bajo presión conserva colores, tamaño y controles; la interacción del dial y el tiempo mostrado permanecen operativos. Al volver de la suspensión se conserva la advertencia de estado desactualizado hasta recibir una lectura nueva, como exige la seguridad existente. Fallos o ausencia de APIs de recursos no impiden recuperar o abrir la ventana. No se recortan working sets del sistema, no se fuerzan GC, no se cambian prioridades de otros programas ni se autoeleva.

### Limitaciones y siguiente paso

La presión se simula mediante el proveedor de memoria en WebView2 real; no se hace una prueba destructiva agotando la RAM física. La suspensión/liberación es de mejor esfuerzo del runtime y Windows: no hay garantía de un máximo fijo de RAM ni de cero GPU. Se ha reducido el trabajo de renderizado; el porcentaje real de ahorro de RAM/GPU instalado queda pendiente de medir.

**No instalado:** la terminal no está elevada. Siguiente paso: ejecutar el instalador .145 en contexto autorizado/elevado y medir escenarios visible, minimizado/bandeja y presión de RAM en la instalación activa.

Estado: DONE_WITH_CONCERNS (código, pruebas y paquete completos; instalación y comparación de consumo instaladas pendientes por privilegios).
