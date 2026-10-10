# FG-213 — Plan: objetivo opcional para sesiones nuevas en React

## Objetivo

Dar a quien inicia una sesión normal, un ciclo o una sesión strict en el dashboard React la misma nota de objetivo local que Classic ya guarda en `SessionIntention`. Presentarla antes de empezar, persistirla sólo si el Host valida que el servicio aceptó la activación y restaurarla en Repeat.

## Decisiones de diseño

- Campo opcional de una sola nota, máximo 240 caracteres; visible junto a la configuración de sesión, con etiqueta persistente, ayuda “sólo en el journal local” y contador `n/240`.
- Normal/FocusCycle muestran el objetivo dentro de la revisión que precede la confirmación. Strict lo muestra en el segundo paso de su review, junto al resto de las consecuencias; el código sigue siendo el único secreto.
- No animar cambios de texto o teclado: es un campo frecuente y de entrada directa. Mantener paleta/tipografía/tokens locales graphite-mint/Geist, foco visible y el mismo objetivo/copy en inglés y español.
- Aplicar reglas UI/UX Pro Max de formulario etiquetado/foco; Emil y Apple Design de feedback inmediato y no añadir movimiento de bajo valor; usar de Taste sólo la disciplina de leer el brief y evitar patrones prestados, pues explícitamente excluye dashboards; Front-end Design indica conservar la identidad del producto existente.

## Flujo y límites de seguridad

- React controla y restaura el texto, pero no lo manda al preparar la sesión.
- La operación confirmada `session.startNormal`, `session.startCycle` o `session.strict.activate` incluye un campo exacto y acotado a 240 caracteres. El Host lo usa únicamente después de recibir respuesta `Accepted` y validar el sessionId/horario/mode, cuando construye la intención local.
- Respuesta rechazada/timeout/outcome desconocido no registra una nueva intención ni texto. El Host no reintenta activaciones. El objetivo nunca entra a contratos IPC del servicio, snapshots de enforcement, políticas, extensiones, nombres de reglas o logs.
- `session.intent.get` devuelve el objetivo local acotado; React lo muestra como borrador en el nuevo Repeat y todavía vuelve a preparar y revisar la política actual antes de iniciar.

## Verificación

- Pruebas parser .NET: obligatorio en los tres comandos, acepta cadena vacía y 240 unidades, rechaza 241 y propiedades extras.
- Host integration: timer, ciclo y strict persistidos tras aceptación; comprobación del límite entre Host/React y no-start paths sin entrada.
- React DOM: objetivo con etiqueta/privacidad/límite, aparecen en reviews normal y strict, se manda sólo al confirmar y Repeat lo restaura.
- Suites React, Host y Client; `dotnet build` Release, CSP/chunks, Graphify, package 0.1.62 y recursos embebidos verificados. No instalar.
