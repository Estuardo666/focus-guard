# Checkpoint — codec Android para documentos FG-E2EE-1

## Resultado

El probe Android ahora cifra y abre documentos `FG-E2EE-1` compatibles con el codec C# usando libsodium 1.0.22, XChaCha20-Poly1305-IETF, JCS y Ed25519. Se comprobó el flujo completo C#→Android con un sobre firmado por C# y Android→C# con un sobre creado/firmado dentro del AVD. Ambos descifraron exactamente el mismo JSON sintético canónico.

La cabecera JCS firma protocolo, suite, contexto, autor, nonce y ciphertext; la firma se excluye de su propia entrada. La AAD incluye `membershipRevision`, además del resto del contexto, igual al codec C# actual. ADR0022 ahora documenta ese campo, que ya estaba en el código C#.

## Archivos cambiados

- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidSodium.java`: bindings JNA de AEAD XChaCha20-Poly1305-IETF.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidContentDocumentCodec.java`: validación de contexto/revisiones, canonicalización estricta, AAD, cifrado, firma, verificación y apertura.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidE2eeRuntimeProbe.java`: fixture C#→Android, roundtrip Android y negativos criptográficos/JSON; exporta temporalmente sólo el envelope cifrado de ensayo.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/ProbeActivity.java`, `prototypes/cloud-phase0/android/build-probe.ps1`, `prototypes/cloud-phase0/DeviceInterop/Program.cs`.
- `docs/adr/0022-xchacha-content-documents.md`, `prototypes/cloud-phase0/android/ANDROID-VM.md`, `cloud/devices/README.md`, `TASKS.md`, y planes/checkpoints Android.

## Verificación

- Compilación Android Java 17 con `-Xlint:deprecation -Werror`; D8 empaqueta para minSdk26; firma local v2/v3: PASS.
- Instalación y ejecución en el AVD Android 15/API35 `emulator-5570`: PASS.
- C#→Android: la Activity valida un envelope generado por `ContentDocumentCodec.Encrypt`, firma y ciphertext; abre `{"durationMinutes":25,"theme":"mist"}` con la misma clave/contexto: PASS.
- Android→C#: la Activity cifra/firmó el mismo documento, guardó sólo el JSON del envelope en `files/` privado; `DeviceInterop` y `ContentDocumentCodec.Decrypt` lo abrieron en el host C#: PASS. El archivo temporal se borró del AVD al terminar.
- Rechazos Android: membership/contexto distinto, clave incorrecta, firma modificada, ciphertext modificado, suite desconocida, envelope duplicado y revisiones decimales; plaintext con claves duplicadas, no canónico, UTF-8 inválido, raíz que no es objeto, profundidad33 y tamaño>256KiB: PASS.
- `aapt2 dump permissions`: `PACKAGE_USAGE_STATS` únicamente; sin `INTERNET`.
- Huella de identidad y vault se recargaron tras force-stop en pruebas previas del mismo APK/probe.

## Artefactos

- APK firmado de ensayo: `artifacts/cloud-phase0/android-identity-probe/focusguard-phase0-test.apk`.
- SHA-256 APK del probe Android combinado más reciente (documentos, sobres y membresía): `6BA11A964985238C0BFF85E689F0B7DD10913B77BCD70CA366E4B42546592169`.
- Sobre cifrado Android de datos sintéticos para repetición local C#: `artifacts/cloud-phase0/android-identity-probe/android-document-interop.json`. Contiene ciphertext/firma y metadatos públicos, no la clave ni plaintext.
- XML de UI: `artifacts/cloud-phase0/android-identity-probe/e2ee-final.xml`.
- Captura del probe: `artifacts/cloud-phase0/android-identity-probe/android-e2ee-pass.png` (SHA-256 `90E7E0274E6462AFCFE2E3B1D7A7BFBE4BB21E0C62FE0FA701C9B08012889F69`).

## Seguridad y límites

El probe verifica la firma del autor antes de AEAD, limita entradas, verifica codificación base64url canónica y borra buffers temporales. Firma válida no basta para autorizar al emisor: el cliente productivo aún debe verificar que el dispositivo era miembro activo en `membershipRevision`. Aún no hay API Android, pairing/aprobación, cuenta real, sincronización, CAS, recuperación ni UI de preferencias. El APK es sólo de ensayo, sin red ni bloqueo. El Keystore y la ABI son del AVD x86_64 con backend software; falta validar Samsung/ARM64 y revisar distribución/carga nativa para producción. Fase 2 continúa abierta.
