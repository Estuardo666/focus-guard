# FG-278 — Fase 5: smokes del Host .94 instalado

## Resumen

Ejecuté los tres escenarios smoke del FocusGuard.UI.exe instalado en C:/Program Files/FocusGuard. Los tres terminaron con exit code0: carga del contenido local, recuperación simulada de renderer failure y crash real del renderer de la instancia smoke con recuperación.

## Estado del producto

Antes y después, DrillClient devolvió ServiceReady e Inactive sin SessionId. El servicio permaneció Running como LocalService con PID18792. La ventana normal Host PID38352 siguió abierta y ProcessAgent PID45296 siguió siendo hijo suyo. Los smoke no iniciaron Agent ni enviaron solicitudes de servicio; usaron composición no integrada y carpetas temporales.

## Comandos y evidencia

Ejecutables invocados secuencialmente:
- C:/Program Files/FocusGuard/FocusGuard.UI.exe --smoke
- C:/Program Files/FocusGuard/FocusGuard.UI.exe --smoke-renderer-failure
- C:/Program Files/FocusGuard/FocusGuard.UI.exe --smoke-renderer-crash

Resultado por proceso y estado pre/post: D:/TEMP/focusguard-fg166-validation/artifacts/fg278/installed-host-smokes-0.1.94.json.

## UX y seguridad

No se inició ni detuvo sesión, no se cambió la política y no se lanzó TestTarget. El renderer crash ocurrió sólo en el WebView2 aislado del smoke; la ventana normal y el service permanecieron vivos. No se capturó una imagen de la ventana normal.

## Límites pendientes

Los smokes no comprueban la interacción normal con el backend autenticado, el dashboard activo, la continuidad al cerrar/reabrir durante Active, accesibilidad, teclado/foco, forced-colors, DPI ni Edge. La aceptación visual de FG272/FG273 sigue pendiente.

## Siguiente tarea

Aceptar visualmente el Host .94 normal y completar el recorrido de sesión activa con cierre/reapertura antes de cerrar Fase 5/7. Fase4 wrong-role/rollback y Fase9 carga sostenida siguen abiertos.