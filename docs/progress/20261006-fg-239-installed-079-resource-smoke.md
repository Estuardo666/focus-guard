# FG-239 — Muestra de recursos del Host .79 instalado

## Método

Se midió durante10s el Host React .79 instalado, con la ventana en piloto, servicio LocalService activo y el timer de prueba ya expirado. El script `scripts/measure-focusguard-resources.ps1` siguió el árbol WebView2 y tomó una muestra de CPU, memoria y número de procesos. El paquete .79 instalado se validó antes por SHA-256.

## Resultado

- Host tree:7 procesos, 419.38 MB working set, 207.65 MB private memory.
- CPU agregado de FocusGuard:0.013%; 12 procesadores lógicos.
- Service:69.14 MB working set/26.80 MB private, 0% CPU.
- Los tres gates pasan: `serviceWithinBudget`, `singleDesktopInstance`, `singleReactHostInstance`.
- FocusGuard completo:11 procesos/766.48 MB working set/434.31 MB private. Incluye NativeMessaging, Desktop y ProcessAgent activos.

## Alcance

Es un smoke instalado en reposo, no carga sostenida ni comparación controlada con .77. La métrica no prueba rendimiento durante bloqueo activo. Reporte: `D:\TEMP\focusguard-fg166-validation\artifacts\performance\react-host-0.1.79-installed-pilot-idle.json`; SHA-256 `2706D5A921A1908E85441A0BCEA534C64572D93CC052D5EA514495B246A4829E`.
