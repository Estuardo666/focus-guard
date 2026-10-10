# FG-393 — humo de foco por teclado en Host .135

## Resultado (2026-10-09)

En el Host React instalado .135, ventana 1216×799, los siete `tab items` de navegación reportan `IsKeyboardFocusable=true`. Tab llevó el foco desde Perfiles por los controles del encabezado, Actualizar perfiles, las cinco fichas de perfil, Nombre del nuevo perfil y las opciones del perfil; UI Automation confirmó el control enfocado en cada paso. Con la pestaña Perfiles enfocada, la flecha Abajo seleccionó Actividad y el panel cambió a esa vista. Volví a Resumen al terminar.

Servicio Conectado, Extensión Disponible y Sin sesión activa. No activé controles ni edité campos/reglas. El archivo de preferencias conserva SHA `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55`.

Esto es un humo de foco en una ventana, no la matriz completa: siguen pendientes recorrido entero de cada vista, activación por teclado de todos los controles, Narrator, forced-colors, DPI real y Windows 10/11.

Evidencia: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\fg393-installed-keyboard-focus-smoke.json`.
