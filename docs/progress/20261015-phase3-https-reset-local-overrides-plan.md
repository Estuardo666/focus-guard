# Fase 3: reset global con overrides locales en HTTPS dual Host — plan

## Brecha

La cobertura dual Host verifica los tres overrides frente a una revisión remota ordinaria. El reset/tombstone y la preservación de override tienen pruebas locales separadas; falta comprobar ambos juntos en el flujo Host→HTTPS.

## Entrega prevista

Extender la prueba dual Host para guardar overrides locales de tema/idioma/duración, publicar un reset compartido y verificar que el tombstone converge en el peer mientras el propietario conserva sus tres valores locales. Después, retirar los overrides y confirmar que el propietario aplica los defaults ya confirmados sin generar otro POST o revisión.

Seguridad y recuperación: el reset modifica únicamente los tres valores globales y conserva locales, consentimiento, membresía y estado de sesión. La lectura del outbox y la proyección local se esperan por separado porque el scheduler confirma antes de proyectar.

## Aceptación

Prueba HTTPS fijada con dos sesiones Host y outboxes DPAPI independientes; Host suite completa, formato y graphify. Solo cobertura local: no instalar ni desplegar, y no reemplaza la aceptación física pendiente.
