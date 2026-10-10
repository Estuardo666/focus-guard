# FG-265 — Plan: diferenciar recarga de perfiles y estado del servicio

## Lectura de diseño

Rediseño incremental de herramienta de productividad Windows para preparar sesiones de enfoque: conservar graphite/mint, Geist, jerarquía accesible y densidad actual; hacer que cada acción diga qué datos actualiza. Skills aplicadas: UI/UX Pro Max, Emil Design Engineering, Taste (brief/inferencia y disciplina anti-default), Frontend Design y Apple Design. La búsqueda genérica de UI/UX devolvió un patrón de landing/comparación y tipografía externa; se descarta por incompatibilidad con la app nativa, el contrato visual local y el uso offline.

## Hallazgo y plan

La pantalla instalada «Perfiles» tiene dos acciones distintas llamadas «Actualizar estado»: la global solicita el estado de conexión y la del rail ejecuta `reload()` para recargar catálogo y detalle de perfiles. En el rail de180px a1216px, el texto de la segunda se comprime y parte en varias líneas.

1. Cambiar la acción del rail a «Actualizar perfiles» / «Refresh profiles» y conservar «Actualizar estado» / «Refresh status» para la conexión.
2. A ≤1300px, apilar título y botón del rail, haciendo que la etiqueta específica quepa en una línea; conservar el encabezado horizontal a tamaños amplios.
3. Agregar regresión de copy EN/ES y revisar que el reload siga habilitado sólo cuando corresponda.
4. Ejecutar la suite React, TypeScript, build/CSP/contraste y validaciones de paquete; generar el Setup/ZIP independiente .91.
5. Instalar owner-present y comparar capturas a1216px con la baseline, sin cambiar perfiles, reglas ni estado de sesión.

## Aceptación, riesgo y recuperación

Aceptar cuando el botón del rail se nombra por los perfiles, la acción global sigue nombrando el estado, y la captura instalada no envuelve el botón ni altera la jerarquía visual. La modificación toca sólo copy y CSS local; no cambia protocolo, IPC, perfiles, reglas ni autorización. Si el paquete falla, conservar la .90 instalada como rollback y no iniciar sesiones.

Baseline: `D:\TEMP\focusguard-fg166-validation\artifacts\fg265\installed-host-profile-0.1.90-screen.png`.
