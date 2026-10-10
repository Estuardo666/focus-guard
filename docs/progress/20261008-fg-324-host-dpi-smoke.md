# FG-324 — Smoke de DPI del Host WebView2

Fecha: 2026-10-08
Estado: smoke implementado y validado en el paquete .110; cambio real de monitor instalado pendiente.

## Problema

El Host declara `PerMonitorV2`, `AutoScaleMode.Dpi` y un mínimo nativo de720×480, y existe una prueba de cambio a modo compacto. El paquete no tenía un smoke ejecutable que verificara el contexto DPI real del HWND creado por el proceso Host.

## Plan

1. Añadir `--smoke-window-dpi` al parser de smokes del Host.
2. Crear un MainForm local sin mostrarlo ni inicializar WebView2, Agent o pipes; verificar awareness context PerMonitorV2 del HWND, DPI positivo igual a `DeviceDpi`, autoscale por DPI y mínimo720×480.
3. Probar el nuevo flag en el ejecutable publicado .110 y conservar los cuatro smokes existentes.
4. Mantener separada la aceptación visual instalada, cambio real de monitor, Windows10/11 y escalas múltiples.

## Aceptación

El smoke del ejecutable empaquetado sale0 bajo el token actual y valida el HWND de la aplicación real. No cambia servicio, extensión, sesión ni datos de owner. La prueba no afirma que se recorrió un cambio real de monitor/DPI.

## Implementación y verificación

- `src/FocusGuard.Host.Windows/Program.cs` acepta `--smoke-window-dpi`; crea el HWND de `MainForm` pero retorna antes de `Application.Run`, por lo que no inicializa WebView2 ni llama al Service.
- Comprueba que el awareness context del HWND sea PerMonitorV2, que `GetDpiForWindow` coincida con `DeviceDpi`, `AutoScaleMode.Dpi` siga activo y que el mínimo, normalizado a96dpi, sea720×480 DIP.
- `scripts/test-installer.ps1` verifica que el smoke y las APIs/contratos sigan incluidos.
- Ejecutables Host publicados .110: `--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure`, `--smoke-renderer-crash` y `--smoke-window-dpi` salieron0.
- Suite .NET877/877, React118/118, extensión19/19; browser/installer tests pasan. Paquete .110: manifest31/31, UI18/18 hashes y Setup/ZIP verificados. Recibo `artifacts/fg324-build-receipt.json`.

Limitación: la comprobación corre a la DPI del escritorio actual. No valida un cambio real de monitor, escala125/150/200%, capturas instaladas ni Narrator; Fase9 sigue abierta para esa matriz.
