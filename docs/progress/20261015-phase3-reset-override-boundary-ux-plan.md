# Fase 3: explicar overrides en la confirmación de reset — plan

## Brecha

La confirmación y el conflicto del reset enumeran los tres valores compartidos, pero no dicen que los overrides «Kept on this PC» siguen activos y pueden hacer que el valor efectivo de ese equipo difiera del reset global.

## Entrega prevista

Actualizar los textos EN/ES del diálogo nativo y de la revisión de conflicto. Explicar en lenguaje directo que el reset afecta solo los valores compartidos, que los overrides locales conservan prioridad y que al quitarlos aparece el valor global vigente. Conservar las advertencias de sesiones activas y reglas/permisos locales.

Seguridad/recuperación: no cambiar política ni datos; prevenir que una persona interprete la confirmación del reset como eliminación de sus overrides. Añadir aserciones de UI para ambas traducciones y empaquetar la siguiente versión candidata.

## Aceptación

UI tests/TypeScript, suite Host, formato, build del instalador, guard de cliente, test-installer, self-test y graphify pasan. No instalar ni desplegar.
