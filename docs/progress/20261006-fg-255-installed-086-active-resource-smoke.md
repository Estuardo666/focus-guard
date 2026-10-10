# FG-255 — Muestra de recursos del Host .86 en sesión normal

## Método
Ejecuté scripts/measure-focusguard-resources.ps1 durante60s sobre el Host .86 instalado y la sesión normal que ya estaba activa. La muestra es de solo lectura: no envía Activate/Stop, no cambia perfil ni reglas y no redimensiona ni cierra la ventana. La SessionId comprobada antes y después fue4b203b6b-f6c4-4554-a6fb-4f8153945330.

## Resultado
- Árbol React/WebView2:7 procesos,416.13 MB working set y203.76 MB private.
- FocusGuard completo:11 procesos,795.67 MB working set,459.91 MB private y0.4166% CPU agregado en12 CPU lógicas.
- Service .86:PID30976,76.45 MB working set,35.57 MB private y0.000% CPU.
- Gates serviceWithinBudget, singleDesktopInstance y singleReactHostInstance pasan.
- ServiceReady, ExtensionReady y EnforcementConnected fueron true antes y después; StrictMode false.

## Alcance
Mide un Host instalado con sesión normal activa, pero no inició la sesión ni verificó bloqueo real de una aplicación o URL. Tampoco es una prueba de carga sostenida. El JSON completo, resumen y hashes están en D:\TEMP\focusguard-fg166-validation\artifacts\fg255.