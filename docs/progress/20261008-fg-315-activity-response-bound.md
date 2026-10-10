# FG-315 — Respuesta Activity acotada al envelope IPC

Fecha: 2026-10-08

Estado: corrección fuente y paquete .104; revalidación instalada pendiente.

## Observación instalada

El DrillClient .102 `status` autenticado respondió `serviceReady=true`, `extensionReady=true`, `sessionState=Expired`. Una consulta `activity` cerró el pipe; una segunda consulta `status` confirmó el mismo estado de sesión, y el Service permaneció Running como LocalService con PID25096. El artefacto conserva sólo el código de salida, recuento (nulo al fallar), estado y continuidad; no guarda destinos ni contenido de actividad.

## Reproducción y causa

La prueba de integración genera 200 eventos sintéticos válidos, cada uno con destino y resultado largos, y los consulta por el pipe de control. Antes del fix, el cliente leía EOF: el payload JSON excedía el límite de 64KiB. `CreateActivityResponse` devolvía error de tamaño; `HandleConnectionAsync` elevaba `InvalidDataException`, que el listener aislaba cerrando el pipe sin fallar el servicio. Esto reproduce el mismo síntoma. No se pudo leer la actividad real del usuario en la máquina ni demostrar que ese conjunto exacto de eventos .102 fuese el que excedía el límite.

## Cambio

El handler prueba el tamaño final del envelope y usa búsqueda binaria para devolver el prefijo más reciente que cabe. `ActivityRequest.MaximumCount` se documenta como techo: bajo el límite IPC, pueden devolverse menos elementos, conservando el orden más reciente primero. No se cambia el endpoint ni se escribe sesión, política o historial.

## Verificación

- Antes del fix, `LargeRecentActivityResponseFitsTheBoundedControlEnvelope` falló al recibir EOF con el conjunto sintético de 200 eventos.
- Después del fix, la prueba pasa: una solicitud pequeña devuelve todos los eventos pedidos; la grande devuelve una respuesta válida dentro de64KiB, conserva el evento más reciente y el servicio sigue `Inactive`.
- `dotnet test FocusGuard.slnx -c Release --no-restore`: 14 ensamblados, 878 pruebas aprobadas, cero fallos. El build React .104 integrado en Host valida también contraste, recursos locales, CSP y viewport720×480.
- El paquete standalone .104 se generó y está pendiente de aceptación instalada; la instalación .102 no se modificó.

Siguiente verificación: generar/validar .104, luego repetir `status` y `activity` con el DrillClient instalado .104 bajo LocalService y confirmar que el estado de sesión sigue igual.
