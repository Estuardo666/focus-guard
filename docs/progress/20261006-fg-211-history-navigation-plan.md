# FG-211 — Plan: navegación diaria del historial

## Objetivo

Ampliar Activity para consultar días anteriores desde la UI React mediante el `today.get` tipado existente, sin cambiar el modelo autoritativo del servicio ni inventar datos.

## Diseño

- La fecha inicial es el día local actual.
- Anterior/siguiente cambia una fecha de calendario local y calcula medianoches locales consecutivas antes de convertir el intervalo a UTC. Esto mantiene el día civil correcto incluso en zonas con cambio de horario.
- No se permiten fechas futuras. Las acciones se deshabilitan durante la consulta y al estar seleccionado hoy para Siguiente.
- La etiqueta de fecha, los agregados y la cronología se presentan juntos. Al cambiar día se cierra el detalle anterior; mientras carga o tras un error no se muestran agregados de otra fecha.
- El servicio sigue limitando `today.get` a intervalos UTC de hasta dos días y la UI solicita exactamente un día. El contrato/autoridad del servicio no cambia.
- Seguir el contrato de diseño local: tokens semánticos, Geist, español/inglés, teclado, foco visible, targets de 44px, sin movimiento que retrase una acción inmediata.

## Verificación

- Pruebas DOM: rango inicial local convertido a UTC, navegación a día anterior/siguiente, límite para no entrar al futuro, resultados asociados a la fecha y error sin datos obsoletos.
- Suite React, typecheck, bundle/CSP/chunks; suite Host y Release solution.
- Generar paquete nuevo 0.1.60 en rutas no existentes, comprobar payload y ZIP; no instalar.
