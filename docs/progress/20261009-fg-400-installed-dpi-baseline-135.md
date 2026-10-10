# FG-400 — baseline DPI del Host instalado .135

## Resultado (2026-10-09)

Lectura solo de consulta del HWND del Host instalado `FocusGuard.UI.exe` PID 35304: `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2=true`, awareness `Per-Monitor`, `GetDpiForWindow=96`, `GetDpiForSystem=96`, ventana 1216×799 en posición 352,155. No cambié escala ni moví la ventana.

Esto confirma el baseline del monitor actual en .135, no un cambio real de DPI/monitor, la matriz Windows 10/11 ni Narrator/forced-colors.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg400-installed-dpi-baseline-135.json`.
