# Plan — sesión OIDC móvil y contexto de cuenta Android

## Objetivo

Pasar del probe criptográfico aislado a un APK Android instalable que inicie sesión contra Keycloak con navegador externo, Authorization Code + PKCE, guarde `AuthState` dentro del vault Keystore y consulte `/v1/bootstrap/account` con un bearer token que nunca sale del módulo nativo.

## Decisiones y límites

- Cliente público separado `focusguard-android`; sin client secret, password grant ni formulario de contraseña en la app.
- Redirect URI exacta `com.totemmassmedia.focusguard:/oauth2redirect`, scheme inverso de dominio controlado, no wildcard.
- AppAuth Android 0.11.1 (`Apache-2.0`) para el flujo OIDC/PKCE externo; pin de AndroidX Browser/AppCompat/Annotation a versiones estables actuales. La versión AppAuth no tiene una release reciente; se usa para un prototipo revisable y queda bloqueada como gate de producción hasta evaluar mantenimiento/advisories o alternativa.
- La sesión se cifra con `AndroidDeviceVault` en `noBackupFilesDir`; una sola cuenta nativa a la vez. `sub` y correo se validan contra Keycloak `userinfo`; cuenta/dispositivo se obtienen por API sobre HTTPS.
- El portal web y el Host Windows existentes no se cambian. El MVP no ejecuta bloqueo; sólo presenta identidad y contexto de cuenta para la siguiente fase de pairing.
- Logout borra primero la sesión local y luego intenta revocar el refresh token remoto; un error de red no debe restaurar la sesión local.

## Aceptación de este incremento

- Gradle wrapper reproducible, distribución verificada por SHA-256, dependency locking y verification metadata.
- APK staging se instala en el AVD Android15/API35; permisos estrictamente necesarios y backup deshabilitado.
- Login usa navegador, state/nonce/PKCE de AppAuth; no muestra ni recibe contraseña en la app.
- `userinfo` y API sólo se llaman al issuer/API exactos HTTPS, sin seguir redirects; respuestas y body están acotados.
- `AuthState` se guarda/lee desde el vault cifrado; no hay tokens en UI/logcat o en archivos planos.
- Perfil/estado de cuenta se sanitiza; no se presenta la cuenta vinculada como si este teléfono ya estuviese aprobado.
- Logout borra fichero y alias Keystore local aunque el endpoint remoto falle.

## Secuencia

1. Crear módulo Gradle Android Java 17 (AGP8.13/Gradle8.13, API35/min26), incorporar sólo `AndroidDeviceVault` del probe y AppAuth.
2. Añadir almacenamiento `AuthState`, OIDC por Custom Tabs y recuperación/rotación de tokens.
3. Consultar userinfo y `/v1/bootstrap/account`; mostrar el teléfono como pendiente hasta pairing real.
4. Añadir cliente Keycloak separado y redirect exacto al realm template/provisioner; aplicar config de staging de forma controlada.
5. Compilar e instalar en AVD; probar cancelación, flujo autenticado cuando la interacción del propietario esté disponible, restore/refresh/logout y permiso mínimo.
6. Registrar si el flujo real requiere que el propietario complete el login en la UI del emulador; continuar luego con solicitud/confirmación de pairing y sobres de clave.
