# FG-224 — Acción principal siempre visible en Programación

## Plan

- Seguir `UX_SPEC.md` y el contrato de diseño: la confirmación principal de un flujo largo debe quedar visible mientras el contenido secundario se desplaza.
- Cambiar sólo la estructura/layout del diálogo ScheduleEditor: cuerpo desplazable con nombre accesible; pie separado con Cancel/Review/Confirm siempre visible.
- Mantener intactos CAS, validación, revisiones, permisos del piloto, Service y almacenamiento.
- Añadir una regresión DOM que pruebe la relación form/footer y que cancelar no envíe `schedule.save`.
- Ejecutar React tests, Release build y crear/validar el Setup standalone 0.1.72. No instalar sin token elevado existente.

## Aceptación

1. A 1216×799 el pie de acciones no depende de desplazarse al final del formulario.
2. El cuerpo conserva scroll y navegación de teclado; el pie sigue dentro del diálogo accesible.
3. Review y Confirm siguen enviándose al formulario/handlers actuales; Cancel no persiste.
4. ES/EN, high contrast, reduced motion, reglas y permisos quedan sin cambios.
5. Tests, CSP/local assets, Release build y package manifest pasan.

## Riesgo y límite

Sólo React/CSS, sin cambios de protocolo, sesión, horarios ni perfil. La aceptación visual instalada se hará al actualizar con el paquete generado; el navegador temporal sigue pendiente por la política de proceso.