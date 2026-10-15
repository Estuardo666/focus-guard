# Fase 3: integración de preferencias exclusivas de esta PC

## Contrato

Para tema, idioma y duración, cada campo puede seguir el ajuste compartido o conservar el valor local elegido. El usuario debe distinguir una excepción local de un cambio que llegará a los demás dispositivos. No se incluyen rutas, perfiles ni permisos. Precedencia del plan: sesión activa y consentimiento antes de override local, después ajuste global/default.

## Persistencia y proyección

- Conservar la selección y el valor del override en el estado local DPAPI por cuenta/generación/dispositivo. Los documentos globales cifrados siguen conteniendo exclusivamente los tres ajustes globales; no deben transportar overrides.
- Leer el override vigente al proyectar cada revisión, al resolver conflictos y al aplicar reset. Evitar capturar una copia obsoleta durante la construcción del runner.
- Guardar/cambiar un override conserva Confirmed/Draft/InFlight/cursor y no publica ni habilita consentimiento. Quitar un override aplica el último valor compartido/draft verificado, sin publicarlo como edición nueva.
- Una edición del campo que tenga override actualiza solo el valor local. Una edición mixta publica únicamente los campos sin override. No reconstruir el documento compartido desde preferencias efectivas porque filtraría valores locales.
- Un fallo de proyección después del commit conserva estado durable para reintentar; corrupción no se sustituye por defaults. Mantener el orden de leases outbox → preferencias de las rutas existentes.

## Superficie Windows pendiente

Acción bridge sin valores arbitrarios abre diálogo nativo con tres elecciones: seguir compartido o mantener en esta PC. El diálogo muestra los valores efectivos y el compartido cuando se desactiva una excepción. La confirmación debe revalidar revisión/override vistos; cancelar no escribe. La tarjeta de sincronización resume los campos locales y explica que quedan fuera de la sincronización. EN/ES y navegación por teclado obligatorios.

## Pruebas de aceptación local

Persistencia/reinicio; cambio remoto conservando override; retirar override aplica remoto; edición solo local sin POST; edición mixta sin fuga; reset/tombstone y resolución de conflictos conservan excepción; cancelación/stale-review sin escritura; preferencias no compartidas conservadas; consentimiento revocado no reactiva worker. Después: integrar con el flujo portátil y regenerar instalador antes de aceptar el producto.

Estado: implementación de almacenamiento/proyección delegada; integración Host/bridge/UI todavía pendiente. Este documento no prueba el comportamiento del producto.
