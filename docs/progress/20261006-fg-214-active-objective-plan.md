# FG-214 — Plan: objetivo visible durante la sesión activa

## Objetivo

Completar el recorrido de la nota local: una vez iniciada una sesión, mostrar el objetivo de `SessionIntention` asociado al mismo `sessionId` que el servicio reporta.

## Diseño

- Cuando el estado del servicio sea `Active` o `Activating`, la UI consulta `session.intent.get` si aún no tiene intención para el sessionId vigente.
- Comparar los ID de manera case-insensitive. Mostrar el texto sólo desde un DTO válido, disponible y del ID exacto; una intención ajena/no disponible nunca debe filtrarse a la sesión actual.
- Presentar el objetivo como texto secundario debajo del perfil. No añadir control de sesión ni usar el dato para inferir tiempo, fase, bloqueo o autoridad. Strict sigue sin ofrecer botones de salida.
- Evitar polling y movimiento: reconsultar sólo al cambiar el ID/estado activo y respetar el mismo contrato local graphite/mint, Geist y layout de la pantalla.

## Verificación

- Prueba DOM activa: solicita intent por el ID del servicio, acepta sólo el mismo ID, presenta su objetivo y no emite prepare/start/strict activation.
- Ejecutar todas las pruebas UI y Host, `dotnet build` Release, Graphify y paquete 0.1.63 con recursos/hash correctos. No instalar.
