# FG-390 — aceptación exacta del borrador Repeat FG386

## Resultado (2026-10-09)

En el Host React instalado .135 abrí en Actividad el resumen con `SessionId=6e200394-0fab-45bd-8711-bf3145ebaa8b` (perfil `2`, horario 16:45–16:46) y usé «Revisar y repetir». UI Automation confirmó `Modo estricto=Off`, `Activar bloqueo=Off`, perfil `2`, duración `1` minuto y CTA «Revisar sesión». El Host informó Servicio Conectado, Extensión Disponible y «Sin sesión activa». No confirmé ni inicié una sesión y no edité perfiles o reglas. La ventana quedó restaurada a 1216×799.

Esto resuelve la duda visual: el borrador del registro FG386 exacto es no estricto y timer-only. La casilla activada vista antes pertenecía a un borrador ambiguo anterior; no se usó para aceptar FG386. No se reprodujo un defecto en el Host.

Evidencia estructurada: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg390-exact-repeat-strict-state.json`.
