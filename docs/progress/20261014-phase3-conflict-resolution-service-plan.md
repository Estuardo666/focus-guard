# Fase 3 — plan del servicio de resolución de conflictos

## Alcance

Encapsular persistencia de una elección explícita del usuario y wake-up del runtime. La operación recibe la revisión y campos que la UI mostró; si el outbox cambió mientras el diálogo estaba abierto, rechaza la respuesta obsoleta.

## Aceptación

1. Exigir confirmación explícita y selección válida.
2. Comparar la revisión remota y máscara de campos en conflicto actuales con las que revisó el usuario.
3. Guardar la resolución bajo el lease del vault antes de notificar al runtime.
4. No notificar ni cambiar estado si la confirmación falta o la revisión/conflicto quedó obsoleto.
5. Probar que el wake-up inmediato ocurre solo después del commit local.

## Límite

El servicio no presenta UI ni decide qué valores elegir. No se conecta al Host; la futura pantalla debe mostrar ambas versiones y llamar al servicio solo tras que la persona seleccione.
