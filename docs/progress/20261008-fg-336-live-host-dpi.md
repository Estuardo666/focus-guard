# FG-336 - DPI del Host instalado en el monitor actual

**Fecha:** 2026-10-08
**Fases:** 5 y 9
**Estado:** muestra en vivo del Host .112; cambio de escala y monitor aún pendiente.

## Medición

Se consultó el HWND `0x270822` del Host instalado, sin mover ni redimensionar la ventana:

- Host PID 23972, ventana `FocusGuard`, sesión 1, ruta `C:\Program Files\FocusGuard\FocusGuard.UI.exe`.
- `GetWindowDpiAwarenessContext` coincide con Per-Monitor V2.
- `GetDpiForWindow` devuelve 96 DPI.
- Área exterior: 1216×799 px. Área cliente: 1200×760 px.
- Sistema: Windows 10 Pro, versión 10.0.19045, build 19045.
- Service/Extension Ready, sesión Inactive, sin SessionId, Strict desactivado antes y después.

Recibo: `artifacts/fg336-output/fg336-live-dpi.json`.

## Alcance

Esta muestra confirma el contexto DPI real del HWND y el tamaño cliente en el monitor actual. No cambia la escala del sistema, no comprueba una transición entre monitores o escalas, no valida Windows 11 ni reemplaza la inspección visual/Narrator/forced-colors.
