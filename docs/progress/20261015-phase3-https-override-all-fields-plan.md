# Fase 3: overrides de los tres campos en HTTPS dual Host — plan

## Brecha

La integración dual Host cubre persistencia y precedencia de un override de tema. El plan sincroniza tema, idioma y duración; cubrir solo tema deja sin evidencia integrada la precedencia de los otros dos campos y su proyección al quitar los overrides.

## Entrega prevista

Ampliar el mismo escenario HTTPS/DPAPI para que un Host conserve overrides locales de tema, idioma y duración mientras el peer publica valores globales distintos en los tres. Tras reconstruir ambas sesiones, retirar las excepciones y comprobar que se aplican los tres valores globales actuales sin otro POST ni revisión.

Seguridad y recuperación: las excepciones permanecen en el outbox DPAPI scoped del propietario; el peer publica solo su selección global; reglas, enforcement, movimiento reducido y notificaciones no cambian.

## Aceptación

La prueba demuestra los tres valores compartidos en el peer y los tres overrides locales en el propietario después del reinicio; al retirar overrides, el propietario adopta tema/idioma/duración globales exactos sin draft, Pending o POST. Host suite completa y formato PASS. Solo cobertura, sin instalador, instalación o staging.
