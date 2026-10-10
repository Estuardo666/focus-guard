# FG-348 — Ayuda y modo compacto desde el Host instalado

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 8.6–8.7, Ayuda y compacto  
**Estado:** navegación de Ayuda y entrada/salida de compacto aceptadas en Host .112.

## Resultado

Con Service conectado, Extension disponible y sin sesión activa, la pestaña Ayuda mostró accesiblemente qué sucede al cerrar la ventana o perder la conexión, cómo se administran perfiles/horarios, las reglas durante descansos y la ruta de recuperación estricta. La copia aclara que cerrar la ventana no detiene la sesión y que recuperación requiere administrador, código y espera mínima de 15 minutos.

Desde Ayuda, «Vista compacta» redujo la ventana de 1216×799 a 496×379. El contenido conservó «Sin sesión activa», «Servicio conectado», la acción para volver a la vista completa y la instrucción de que cerrar no detiene una sesión. Volver a la vista completa restauró 1216×799 y la pestaña Ayuda.

No se inició ni detuvo ninguna sesión, y no se modificaron reglas ni preferencias.

## Evidencia

- `artifacts/fg348-ui-acceptance/fg348-help-0.1.112.png`.
- `artifacts/fg348-ui-acceptance/fg348-compact-0.1.112.png`.

Esta pasada no valida modo siempre visible, bandeja, toasts, teclado/Narrator, forced-colors ni Windows 11. El Host instalado sigue en .112.
