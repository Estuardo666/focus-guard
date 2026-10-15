# Fase 2 — solicitud de vinculación desde Android

Plan: [20261012-android-pairing-request-plan.md](20261012-android-pairing-request-plan.md).

## Entregado

El módulo `mobile/android` ahora incluye un APK Android de ensayo con pantalla de cuenta OIDC y el primer tramo real de solicitud de vinculación para un teléfono que inicia sesión en una cuenta que ya tiene un dispositivo FocusGuard aprobado.

- El inicio de sesión usa AppAuth con navegador externo; no añade un formulario ni una contraseña a FocusGuard.
- La primera solicitud crea una identidad Ed25519/X25519 independiente, persiste sus claves dentro de `AndroidDeviceVault`/Android Keystore y obtiene del API un challenge firmado `FG-PAIRING-1`.
- El ID del challenge se guarda cifrado antes del POST de vinculación. Si se pierde la respuesta, la app consulta ese mismo ID con una prueba firmada `FG-PENDING-REQUEST-1` y no crea de inmediato otra identidad.
- La pantalla muestra la huella calculada localmente, el estado pendiente, vencido o aprobado y permite consultar el estado. La aprobación se describe como incompleta: este incremento todavía no verifica la cadena de membresía, abre el sobre de clave ni confirma la sesión del nuevo teléfono.
- La solicitud declara capacidades Android como `Unsupported`; no afirma bloqueo normal/estricto ni concede `StartStrict`.
- El APK incluye JNA 5.19.1 y libsodium 1.0.22 para `arm64-v8a`, `armeabi-v7a`, `x86` y `x86_64`. Gradle verifica hashes de cada biblioteca copiada desde el paquete NuGet fijado antes del build; la AAR JNA tiene checksum en `mobile/android/gradle/verification-metadata.xml` y las dependencias usan lockfile.

## Comprobación

- `./gradlew --offline --no-daemon connectedDebugAndroidTest`: 2/2 pasan en el AVD Android 15/API35. El test instrumentado carga libsodium 1.0.22, crea/firma/verifica una identidad, rechaza una firma con otro dominio, y guarda/recupera la misma identidad desde el vault cifrado. El segundo test fija el JSON canónico del proof Android al contrato `FG-PAIRING-1`.
- `./gradlew --offline --no-daemon lintDebug connectedDebugAndroidTest assembleDebug`: pasa; Lint no reporta errores ni advertencias. Gradle 8.14.6 se verifica con el SHA oficial; AppAuth sigue fijado, JNA se actualizó a 5.19.1 y AndroidX Test a 1.7.0/1.3.0. Se separó el atributo de barra clara para API27+ y se añadieron reglas explícitas para que Android 12+ no respalde ni transfiera el almacenamiento de la app.
- `app-debug.apk` se instala y abre en el AVD `emulator-5570`; pantalla inicial confirma que el acceso ocurre en navegador externo y no pide la contraseña dentro de la app.
- APK debug: SHA-256 `FD55BC4B033D392AF29557D6EE4980229E82A0B72B1774E8DBBE254F6260A071`; 6.9 MB, min API 26, target API 36. El manifiesto pide sólo `INTERNET` y el permiso de receptor dinámico de AndroidX/AppAuth.
- Evidencia visual: `artifacts/cloud-phase0/android-browser-test/pairing-app-final.png`; árbol de accesibilidad: `artifacts/cloud-phase0/android-browser-test/pairing-app-final.xml`.
- `graphify update .` reconstruyó el grafo (20,527 nodos, 107,342 aristas). `git diff --check` no encontró errores; sólo quedan avisos CRLF de archivos preexistentes.

## Límites actuales

La pantalla de acceso del navegador del AVD abrió el origen de identidad, pero el contenido se quedó en blanco. No se introdujeron credenciales ni se completó un login, no se envió una solicitud a la cuenta real y no se vinculó ningún teléfono. Una consulta HTTPS de solo lectura desde el host confirmó que el servidor devuelve el formulario; eso no valida su renderizado en Android.

La prueba ejecutó libsodium en el AVD x86_64; incluir los otros ABI no demuestra ejecución en el Samsung S24. El APK declara target API36, pero sólo se ejecutó en el AVD API35; el comportamiento de target36 en Android16 queda por probar. Sigue pendiente integrar y probar el flujo de aprobación completo en Android: verificar la membresía, abrir el sobre de clave de cuenta, crear la sesión de dispositivo y confirmar el pairing. También faltan el acceso manual OIDC, la comparación física de huella con Windows, la prueba en Samsung ARM64 y transferencia E2EE de ajustes. Fase 2 continúa abierta; este APK no aplica bloqueo remoto.
