# FG-212 — Objetivo y reflexión local en resumen/historial React

## Alcance

El resumen React ahora presenta el objetivo y la reflexión ya almacenados para el mismo `sessionId` en el journal compartido. Si el objetivo está vacío, no se muestra un bloque vacío. La reflexión opcional se puede editar tanto desde el diálogo de cierre como desde el detalle de sesión en Activity. El usuario guarda el texto explícitamente; el editor queda en ES/EN y tiene límite de 1.000 caracteres.

`session.summary` proyecta las dos cadenas desde `session-journal.json`, limita objetivo a 240 y reflexión a 1.000 caracteres y sólo adjunta una intención cuyo ID coincide exactamente con el resumen del servicio. `session.reflection.save` valida esquema y confirmación, vuelve a verificar que el servicio reconoce el resumen terminado y actualiza el journal bajo el lock compartido con Classic. No envía el texto por IPC del servicio de enforcement, no altera política ni sesión y no cambia otros campos del journal. Si la escritura falla, React conserva el borrador y muestra el error.

Cuando un resumen legítimo no tiene entrada de journal, se crea el placeholder local que Classic ya usa para conservar reflexiones; ese placeholder tiene perfil vacío y consentimiento desconocido, por lo que nunca es una intención reproducible.

## Validación

- `npm test`: 16 archivos, 73 pruebas aprobadas; incluye contrato acotado de summary/reflection y prueba DOM de mostrar objetivo, editar y guardar reflexión.
- `dotnet test tests/FocusGuard.Client.Tests/FocusGuard.Client.Tests.csproj -c Release --no-restore`: 109 aprobadas, incluida actualización de reflexión con un segundo store que conserva objetivo.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 138 aprobadas; incluye proyección por ID, escritura del journal, preservación de metadata, sesión desconocida y parser exacto.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings y 0 errors.
- `graphify update .`: 9,932 nodos, 25,182 aristas, 983 comunidades. JSON e informe actualizados; HTML omitido por límite de visualización.
- Builder creó package0.1.61. Manifest con 29/29 archivos presentes, ZIP con 1,730 entradas y chunks React incluidos. Los Setup standalone/package/publish tienen hash idéntico; la validación del builder comprueba también los recursos Host/UI incrustados.

Setup autónomo (`D:\TEMP\focusguard-fg166-validation\artifacts\fg212\FocusGuard-0.1.61-Setup.exe`): SHA-256 `731F95AB1E14E76F86256AE8EAC991CA9D071F5A2E894A0B5A48CFF5E5B7B2AA`.

ZIP menú (`D:\TEMP\focusguard-fg166-validation\artifacts\fg212\FocusGuard-0.1.61-win-x64-menu.zip`): SHA-256 `7FF9039DC71384550F875BC5A0833B9653E8FCF383461ECF126A2108E12D5811`.

## Pendiente

El paquete fue generado y verificado; no se instaló. El formulario React todavía no permite capturar un objetivo para una sesión nueva, aunque muestra los objetivos Classic/journal que existen. Añadir ese campo y llevarlo por review hasta el journal de sesión aceptada queda como siguiente slice. Aceptación LocalService/recovery, función completa, decisión de Block en descansos y fases 4–10 permanecen abiertas.
