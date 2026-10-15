# Plan — completar el pairing Android aprobado

## Objetivo de este incremento

Pasar de una solicitud `Pending/Approved` visible a un dispositivo Android `Active` sólo después de validar el historial de membresía, abrir el sobre de clave dirigido a este teléfono, guardar la sesión localmente cifrada y confirmar la recepción al API.

## Pasos

1. Extender las respuestas autenticadas de pairing/membresía con la cadena firmada necesaria para validar desde genesis; limitar longitud, revisar revisiones/digests y conservar compatibilidad con clientes existentes.
2. Añadir parser Android estricto para esa cadena y probar firmas, continuidad, cuenta/generación, huella/claves propias, rol `ManageDevices` del firmante y rechazo de `StartStrict` no autorizado.
3. Verificar el sobre `FG-SODIUM-BOX-1` del propio `deviceId`, abrir la clave de contenido, generar un `deviceSession` aleatorio y almacenar clave/sesión/revisión/digest en un vault Keystore distinto del vault OIDC.
4. Confirmar con proof `FG-PAIRING-CONFIRM-1` firmado y preservar el mismo `deviceSession` antes del POST para resolver respuestas de red ambiguas de forma idempotente.
5. Tras reinicio, revalidar sesión/membresía antes de mostrar el teléfono vinculado. No activar bloqueo ni conceder `StartStrict`; reportar permisos/capacidades como no soportados.
6. Añadir pruebas de contrato API y Android instrumentadas para historial, envelope, vault y confirmación; ejecutar los checks apropiados de .NET/Gradle/OpenAPI.

## Criterios de aceptación

- El servidor entrega historial sólo al solicitante OIDC que además prueba la clave pendiente; no filtra solicitudes/equipos de otras cuentas.
- Android no marca `Active` sin membresía/digest/firmas correctas, clave propia exacta, sobre válido y confirmación del server.
- Contenido E2EE y secreto de transporte no salen del proceso nativo ni quedan en texto claro, logs, backup o UI.
- Un timeout/reintento conserva la misma identidad y sesión; expiración, replay, revocación y membresía inconsistente fallan cerrados sin presentar un vínculo válido.
- La prueba manual OIDC, la huella en Windows y el Samsung ARM64 se registran aparte; pruebas sintéticas no se presentan como aceptación física.
