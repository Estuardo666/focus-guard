# FG-273 — Explicar el estado Activating en el Host React

## Evidencia

La UI .91 etiqueta `Activating` como «Activando sesión» y muestra «Sin confirmación» en una tarjeta de protección secundaria. En la aceptación instalada FG270 el servicio permaneció en `Activating` sin ningún canal conectado hasta que expiró. `InMemoryFocusService` transiciona a `Active` cuando enforcement está apagado o cuando algún canal solicitado confirma la política; por lo tanto, `Activating` con enforcement solicitado significa que ningún canal confirmó y el bloqueo no ha comenzado.

## Cambio

Añadir un aviso localizado, visible junto al estado y timer, sólo mientras el servicio informa `Activating`. Usar role=status, tokens de advertencia y estilo de alto contraste; retirarlo automáticamente en `Active`/terminal. No modificar órdenes, countdown ni política.

## Aceptación

React ES/EN tests comprueban el aviso en `Activating`, que desaparece al llegar a `Active`, y que la protección se sigue mostrando como no confirmada. Build/CSP/contraste y paquete siguiente validan el recurso integrado. Captura instalada sigue pendiente.

## Implementación y validación

React muestra un aviso `role=status` junto al estado/timer en `Activating`; una actualización a `Active` lo retira. Copia ES/EN distingue la espera de protección del countdown. Test específico y suite React110/110 pasan; TypeScript/build/CSP/contraste pasan. Setup/ZIP .94 integra el cambio, FG-272 y el supervisor React; manifest31/31, archive1732, Chrome/Edge hashes iguales y Host3 smokes salen0. Recibo `D:\TEMP\focusguard-fg166-validation\artifacts\fg274\build-receipt.json`.

No está instalado: el proceso del Setup .92 continúa abierto, el servicio es .91 y no hay Agent activo. La aceptación runtime y la captura WebView2 quedan pendientes.
