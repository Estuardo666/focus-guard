# FG-211 — Activity con navegación por día local

## Alcance

Activity ahora abre en el día local actual y permite consultar días anteriores con controles anterior/siguiente. Cada consulta usa la fecha de calendario local y convierte sus dos medianoches locales a UTC antes de llamar al puente `today.get`. Esto conserva días civiles completos cuando una zona horaria cambia el reloj. El botón siguiente queda deshabilitado al llegar al día local de hoy.

El rótulo de fecha acompaña los agregados y la cronología. La acción de actualizar repite el día seleccionado. Cambiar de fecha cierra el detalle de sesión seleccionado. Cada consulta limpia primero el resumen previo, de modo que una carga lenta o fallida nunca presenta datos del día anterior bajo otra fecha. El protocolo existente ya limita los intervalos a dos días y marca cronologías truncadas; el nuevo control sólo envía un día por petición.

La cabecera Activity del host es ahora la única cabecera de la vista; el control de fecha reúne anterior/siguiente/actualizar. Usa botones de 44px, nombres accesibles, foco visible, cadenas EN/ES y tokens locales. No se añadieron animaciones ni autoridad nueva.

## Pruebas y validación

- `npm test`: 14 archivos, 70 pruebas aprobadas. La prueba nueva fija una fecha local, comprueba los límites UTC del día inicial y del día anterior, bloquea fechas futuras, comprueba que se borren métricas previas durante navegación/carga fallida y verifica el nuevo resumen.
- `npm run build` (ejecutado por el target del host durante validación): TypeScript, Vite, CSP, recursos locales y allowlist de diez chunks aprobados. Bundle principal: 485.24 kB.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 133 aprobadas.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings y 0 errors.
- `graphify update .`: 9,897 nodos, 25,076 aristas, 984 comunidades. Se actualizaron `graph.json` y `GRAPH_REPORT.md`; el HTML se omite por el límite de 5,000 nodos.
- Vista preliminar local con datos de fixture revisada visualmente a 1280×900: cabecera única, barra compacta de fecha, agregados y cronología mantienen la jerarquía. Captura: `D:\TEMP\focusguard-fg166-validation\artifacts\fg211\ui-review\activity-1280-final.png`. La herramienta no produjo captura a 720px; la verificación de ventana estrecha instalada queda pendiente.
- Builder `scripts/build-installer.ps1 -Version 0.1.60 -OutputDirectory D:\TEMP\focusguard-fg166-validation\artifacts\fg211\package-0.1.60`: 29/29 archivos del manifiesto y 1,730 entradas en ZIP; incluye recursos Host/React y extensión. Las copias del Setup standalone, paquete y Setup publicado tienen el mismo SHA-256. El builder comparó además los hashes de los recursos Host/UI incrustados con los publicados.

Setup autónomo (`D:\TEMP\focusguard-fg166-validation\artifacts\fg211\FocusGuard-0.1.60-Setup.exe`): SHA-256 `0EA636B22A354AB954DD529E44C7C0BDE6FF6C1F696DE910288FBFDADF3020E2`.

ZIP menú (`D:\TEMP\focusguard-fg166-validation\artifacts\fg211\FocusGuard-0.1.60-win-x64-menu.zip`): SHA-256 `D0358A980E8282BA0E86D6BE92D97BE404149C472A9800B1C1C3227C3A3C4DEF`.

## Pendiente

El paquete se generó pero no se instaló. La navegación visual estrecha/host WebView2 instalado, equivalencia del sistema y recuperación real permanecen como gates de Fase 9. Siguen abiertas la decisión de `Block` durante descansos, funciones Fase 8 pendientes y aceptación de Fases 4–10. FG211 completa navegación de historial diario; no completa la migración general.
