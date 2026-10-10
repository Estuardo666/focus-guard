# FG-228 — Baseline de recursos del Host instalado

## Resultado

`measure-focusguard-resources.ps1` ahora incluye `FocusGuard.UI` y recorre sus procesos descendientes por ParentProcessId, incluyendo los procesos WebView2. Admite `-OutputPath` para dejar la muestra fuera de los artefactos de instalación; el validador estático de scripts comprueba su sintaxis.

Se abrió el Host **0.1.71** instalado (`host.js` coincide por SHA-256 con el paquete 0.1.71) como usuario normal y en lectura. Se midieron 5.00 s, y se cerró sólo el PID lanzado; el servicio quedó Running como LocalService. No se inició ni detuvo ninguna sesión. El JSON está en `D:\TEMP\focusguard-fg166-validation\artifacts\performance\react-host-0.1.71.json`.

## Mediciones

- Árbol React Host: 7 procesos (1 `FocusGuard.UI` + 6 WebView2), 395.35 MB working set y 206.31 MB private bytes.
- `FocusGuard.UI.exe`: 72.32 MB working set, 21.56 MB private bytes y 0.44 % CPU durante la muestra.
- `FocusGuard.Service`: 70.38 MB working set, 27.57 MB private bytes y 0 % CPU observado.
- Todos los procesos FocusGuard medidos: 11; 758.53 MB working set; 0.75 % CPU agregado en 12 procesadores.
- Gates existentes: `serviceWithinBudget=true`, `singleDesktopInstance=true`, `singleReactHostInstance=true`.
- JSON SHA-256: `18ED6D24A1A6F578846EF50705F19B5F79A2FB6F429F3F8271F8085BAF742DB6`.

## Límites

Es una muestra corta en un equipo, con Host 0.1.71, no una carga de trabajo sostenida ni una medición del paquete 0.1.75. El árbol de WebView2 aporta la mayoría del uso del Host; debe medirse el mismo escenario después de instalar 0.1.75, y todavía faltan Windows/DPI, sesión activa, desconexiones y rendimiento sostenido.
