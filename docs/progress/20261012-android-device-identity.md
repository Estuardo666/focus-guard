# Checkpoint — identidad local Android compatible con C#

## Resultado

El probe Android ahora crea y restaura un UUID de instalación y dos semillas independientes para Ed25519 y X25519. Deriva las claves públicas con libsodium 1.0.22, calcula huella `FG-DEVICE-1` usando JCS + SHA-256 + base64url canónico y firma con separación de dominio compatible con `DeviceIdentity.Sign`. El registro binario, fijo y versionado que contiene las semillas sólo se persiste dentro del vault Android AES-GCM/Keystore ligado al binding de cuenta; evita codificarlas en strings JSON/base64.

El AVD Android 15/API35 pasó creación limpia, firma, verificación, rechazo de otro dominio y payload alterado, comprobación de binding, vector determinista C#/Android y recarga después de `am force-stop`. También pasaron los probes previos de criptografía y vault. El APK solicita sólo `PACKAGE_USAGE_STATS`; no tiene `INTERNET`.

## Archivos cambiados

- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidSodium.java`: wrapper JNA que exige libsodium 1.0.22.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidDeviceIdentity.java`: creación, derivación, firma, verificación, fingerprint, validación de serialización y ciclo de vida de semillas.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidIdentityRuntimeProbe.java`: checks sintéticos de vault, firma/dominio/tamper e interop.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/ProbeActivity.java`: estado del identity probe dentro de un `ScrollView`.
- `prototypes/cloud-phase0/android/build-probe.ps1`: compila las nuevas clases bajo Java 17 con `-Werror`.
- `prototypes/cloud-phase0/DeviceInterop/Program.cs`: emite vector determinista C# únicamente con campos públicos y estado.
- `prototypes/cloud-phase0/android/ANDROID-VM.md`, `TASKS.md` y este checkpoint.

## Validación ejecutada

- `dotnet run --project prototypes/cloud-phase0/DeviceInterop/FocusGuard.Devices.InteropProbe.csproj -c Release`: PASS; el vector público de C# coincide con constantes verificadas por Android.
- `prototypes/cloud-phase0/android/build-probe.ps1`: PASS; compilación Java con warnings-as-errors, empaquetado APK y firma local de ensayo v2/v3. No es certificado de producción.
- Instalación dirigida sólo a `emulator-5570` tras confirmar AVD `FocusGuard_Phase0_Android15`: PASS.
- `pm clear app.focusguard.phase0` seguido de primer arranque: `IDENTITY_CREATE_PASS`, `IDENTITY_SIGNATURE_PASS`, `CROSS_PLATFORM_CSHARP_VECTOR_PASS`, `VAULT_CREATE_PASS`, `CRYPTO_PROBE_PASS`.
- Force-stop/reapertura: `IDENTITY_RELOAD_PASS`, `VAULT_RELOAD_PASS`; verificación y vector C# siguen pasando.
- `aapt2 dump permissions`: declara `PACKAGE_USAGE_STATS`; no declara `INTERNET`.
- Primer runtime detectó que el decoder base64url sólo aceptaba longitud de clave de 32 bytes; se corrigió para calcular la longitud según el tamaño esperado. La repetición limpia pasó.

## Artefactos

- APK de ensayo: `artifacts/cloud-phase0/android-identity-probe/focusguard-phase0-test.apk`.
- SHA-256 APK del probe Android combinado más reciente (identidad + documentos, sobres y membresía; supersede builds previos): `6BA11A964985238C0BFF85E689F0B7DD10913B77BCD70CA366E4B42546592169`.
- XML de creación/recarga: `identity-create-final.xml`, `identity-reload-final.xml` en el mismo directorio.
- Captura de la pantalla del probe: `artifacts/cloud-phase0/android-identity-probe/identity-pass.png`.

## UX y seguridad

La pantalla muestra sólo marcadores de prueba y los primeros 12 caracteres de una huella pública; no imprime ni exporta semillas. El wrapper limita dominios al conjunto ASCII mayúsculo con guiones, limita el payload a 256 KiB y borra buffers privados temporales. Cargas corruptas o incompatibles fallan cerradas, sin reemplazo silencioso de identidad. La identidad local no concede membresía, sincronización ni bloqueo remoto.

## Limitaciones y siguiente trabajo

El Keystore del AVD x86_64 es software. Aún falta validar en Samsung S24/ARM64, revisar el empaquetado nativo para producción, e integrar identidad Android con pairing/aprobación, sesión autenticada, membresías, sobres de claves y documentos `FG-E2EE-1`. No cambia el APK de producción ni activa bloqueo Android. La Fase 2 permanece abierta y también faltan segundo Windows físico, transferencia de ajustes y reset E2EE.
