# FG-371 — Identidad e identidad DPI del Host instalado .123

## Objetivo

Comprobar en el Host WebView2 instalado que la ventana se ejecuta como el usuario propietario, con integridad media y el contexto DPI esperado.

## Resultado

- Host PID29064, propietario igual al usuario actual, `Elevated=false`, integrity RID8192 (Medium).
- La ventana instalada es Per-Monitor V2 a 96 DPI y mide 1216×799.
- FocusGuard Service permanece Running como `NT AUTHORITY\LocalService`; Diagnostic indica ServiceReady/ExtensionReady true, SessionState `Expired` y Strict apagado.

La comprobación cubre el monitor actual. No valida un cambio real de monitor o escala, ni Windows 11, Narrator o forced-colors.

Evidencia: `artifacts/fg371-output/installed-host-window-dpi-owner.json`.
