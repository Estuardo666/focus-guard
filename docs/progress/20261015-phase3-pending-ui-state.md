# Fase 3 — estado Pending visible en la UI

## Resultado

Los estados de consentimiento y sincronización ya se mostraban en `AccountPanel`, pero faltaban regresiones para algunos pasos del recorrido. El test ahora comprueba que `Enabling` presenta el mensaje de trabajo y deshabilita el botón para evitar envíos duplicados, `Cancelled` explica que no se activó la sync, `Pending` informa que los cambios esperan sincronización, y `Enabled` presenta el estado estable.

Esto mejora el recorrido que usará la prueba física ante confirmación en curso, cancelación o red lenta/desconectada: la persona puede distinguir los resultados del consentimiento, cambios pendientes y sincronización estable. No cambia el producto ni el protocolo.

## Verificación

- `npm test -- --run` — 246 tests en 38 archivos pasan.
- `npm run check` — TypeScript pasa.
- `.173` no cambió; no se instaló ni desplegó software.

## Límite

El test verifica el mensaje de estado, no que dos PCs reales converjan. La aceptación física sigue pendiente y está descrita en el [runbook de Fase 3](../cloud-phase0/PHASE3-PHYSICAL-ACCEPTANCE.md).
