# FG-251 — Muestra instalada de reposo durante 60 segundos

## Método
Ejecuté scripts/measure-focusguard-resources.ps1 con SampleSeconds=60 y salida dirigida a D:\TEMP\focusguard-fg166-validation\artifacts\fg251\focusguard-resources-0.1.79-service-idle-60s.json. El script solo enumera procesos y lee CPU/memoria; no envía comandos al servicio ni cambia estado. La UI React estaba cerrada durante la muestra.

## Resultado
- Cuatro procesos: Desktop, NativeMessaging, ProcessAgent y Service. No había FocusGuard.UI ni árbol WebView2.
- Service: 62.84 MB working set, 22.09 MB private memory, 0.000% CPU.
- Árbol FocusGuard completo: 335.31 MB working set, 212.30 MB private memory, 0.063% CPU agregado, 12 procesadores lógicos.
- serviceWithinBudget, singleDesktopInstance y singleReactHostInstance pasan.
- PID del servicio30976, cuenta NT AUTHORITY\LocalService. Hash del ejecutable de servicio: D3D60BCFD9A1193599A567184FE603E44266F83F9B668DE3329716D2E743669B.

## Alcance
Es una muestra instalada de reposo y dura60s, más larga que el smoke de10s anterior. No mide React/WebView2 porque la UI estaba cerrada y no representa bloqueo activo, uso sostenido ni carga de varias sesiones. No prueba el paquete .86, que continúa sin instalar. El JSON y su SHA-256 5D4120D1BB6C409F668742C20124DB10F8A61AAD67DCD15F09412085962874C se conservan en D:\TEMP\focusguard-fg166-validation\artifacts\fg251.