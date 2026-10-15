# Plan — solicitud Android de vinculación

## Alcance de este incremento

Implementar el lado Android de la solicitud pendiente para una cuenta que ya tiene un dispositivo vinculado. No se intenta completar el alta, abrir la clave de cuenta ni declarar el teléfono activo; el siguiente dispositivo Windows conserva la aprobación manual por huella.

## Plan de implementación

1. Añadir al módulo Android las fuentes ya probadas de identidad/vault, JCS y libsodium con dependencias bloqueadas y hashes por ABI.
2. Crear una identidad de instalación separada de las credenciales OIDC y guardar claves sólo en `AndroidDeviceVault`.
3. Reutilizar `/v1/bootstrap/challenges` y `/v1/bootstrap/pairings`; firmar el formato `FG-PAIRING-1` y limitar la red al host de API HTTPS.
4. Persistir localmente el ID del challenge antes del POST y recuperar el resultado mediante `/v1/bootstrap/pairings/{id}` con `FG-PENDING-REQUEST-1`.
5. Exponer en UI solicitud, huella, estado y vencimiento sin entregar tokens/llaves al UI ni llamar vinculada a una solicitud aprobada.
6. Añadir prueba instrumentada para creación/firma/recarga del vault y forma canónica del proof; compilar, instalar y probar en el AVD autorizado.

## Criterios de aceptación

- Gradle con lockfile y verificación SHA-256; `lintDebug`, build e instrumented tests pasan.
- No se agrega formulario de contraseña, permiso de uso/overlay, respaldo de datos, llamadas en claro ni capacidad de bloqueo Android.
- El ID pendiente sobrevive respuesta perdida sin crear otra identidad; la huella viene de las claves locales.
- La UI describe `Approved` como pendiente hasta verificar membresía, abrir el sobre y confirmar la sesión.
- La prueba manual OIDC y pairing real queda separada de pruebas sintéticas; no se registran credenciales de usuario.
