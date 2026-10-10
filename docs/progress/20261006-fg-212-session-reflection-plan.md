# FG-212 — Plan: notas locales en resumen/historial React

## Objetivo

Portar a la vista React de resumen e historial las notas opcionales que Classic ya mantiene en `session-journal.json`: mostrar objetivo/reflexión asociados a la sesión y permitir guardar o editar la reflexión sin enviar ese texto al servicio de enforcement.

## Diseño

- `session.summary` consulta journal por el mismo `sessionId` del resumen autoritativo; incluye sólo texto limitado (objetivo 240 caracteres, reflexión 1.000). Si falta la intención local, devuelve cadenas vacías.
- `session.reflection.save` acepta sólo `sessionId`, texto de reflexión y confirmación literal. El Host sólo modifica un journal ya asociado a esa sesión, bajo el lock compartido con Classic. La escritura es idempotente y no añade operación de enforcement.
- React muestra el objetivo cuando existe y un editor explícito para la reflexión dentro del mismo detalle que se usa en el cierre y en Activity. Error de guardado conserva el borrador; estado desconocido no afirma que se persistió.
- Las notas siguen siendo opcionales y locales al propietario. No se exponen en IPC del servicio, políticas de app/web ni logs. No se cambia el flujo de activación.
- Seguir tokens, Geist, ES/EN, teclado, foco visible y controles de 44px; no gamificar la reflexión.

## Verificación

- Test de `SessionJournalStore` para actualización atómica de reflexión sin pisar objetivo/último journal y con límite de texto.
- Test Host del parser estricto, denegación de sessionId sin intención y persistencia con ID existente; test DOM que muestra objetivo/reflexión y permite guardar.
- Suite React, Client y Host, compilación Release/CSP, Graphify y paquete nuevo sin instalar.
