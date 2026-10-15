# Checkpoint — sobres firmados de clave de cuenta en Android

## Resultado

El probe Android ya crea y abre sobres `FG-SODIUM-BOX-1` compatibles con `SignedKeyEnvelopeCodec.cs`. Usa `crypto_box_seal`/`crypto_box_seal_open` de libsodium 1.0.22 para envolver la clave X25519 del destinatario y Ed25519 para autenticar el header JCS con dominio `FG-SODIUM-BOX-1` + NUL. Android abre el envelope sólo si coincide con el dispositivo destinatario y verifica firma/contexto antes de descifrar.

El AVD abrió un vector generado por C# y C# abrió un envelope generado por Android con la misma clave de contenido de ensayo. Se rechazaron epoch, generación, membresía, firmante, clave pública del destinatario, firma, ciphertext y suite alterados, además de campos JSON duplicados o desconocidos.

## Archivos cambiados

- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidSodium.java`: bindings JNA de `crypto_box_seal` y `crypto_box_seal_open`.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidDeviceIdentity.java`: apertura sealed-box sólo con la clave privada local derivada de su semilla X25519; valida la clave pública destinataria.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidSignedKeyEnvelopeCodec.java`: forma/parseo del contexto y header, JCS, firma, sellado, verificación previa a apertura y validación del payload `FG-ACCOUNT-KEY-1`.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidKeyEnvelopeRuntimeProbe.java`: vector C#→Android, roundtrip Android y negativos; crea sólo envelope sintético temporal.
- `ProbeActivity.java`, `build-probe.ps1`, `prototypes/cloud-phase0/DeviceInterop/Program.cs`.
- `docs/adr/0020-signed-libsodium-key-envelopes.md`, `prototypes/cloud-phase0/android/ANDROID-VM.md`, README y `TASKS.md`.

## Verificación ejecutada

- Build Android Java 17 con `-Xlint:deprecation -Werror`, D8 para minSdk26 y firma local v2/v3: PASS.
- Instalado en el AVD Android 15/API35 `emulator-5570`; se conservó la identidad local entre la actualización.
- C#→Android: C# creó el envelope, Android verificó el header/contexto, abrió el sealed-box sólo con la clave privada destinataria y obtuvo la clave sintética esperada: PASS.
- Android→C#: Android generó header y ciphertext; C# `SignedKeyEnvelopeCodec.OpenForDevice` abrió la misma clave: PASS (`android-key-envelope-to-csharp`).
- Negativos en Activity: epoch, generación, digest de membresía, firma distinta, identidad pública destinataria distinta, ciphertext, suite, JSON duplicado y campo desconocido: todos rechazados.
- `aapt2 dump permissions`: sólo `PACKAGE_USAGE_STATS`; sin `INTERNET`.
- El APK escribió temporalmente sólo el envelope cifrado en su sandbox. Después de la lectura C# borré los envelopes de documento y de clave del AVD y confirmé que no existen.

## Artefactos

- APK de ensayo combinado: `artifacts/cloud-phase0/android-identity-probe/focusguard-phase0-test.apk`.
- SHA-256 APK combinado final (vault/identidad/documentos/sobres/membresía): `6BA11A964985238C0BFF85E689F0B7DD10913B77BCD70CA366E4B42546592169`.
- Envelope de prueba firmado por Android (ciphertext/metadatos; sin clave ni plaintext): `artifacts/cloud-phase0/android-identity-probe/android-key-envelope-interop.json`.
- UI XML del build combinado final: `artifacts/cloud-phase0/android-identity-probe/android-final.xml`.
- Captura del build combinado final: `artifacts/cloud-phase0/android-identity-probe/android-all-pass.png` (SHA-256 `2681D655002172FACE89C74D6E50ED78438C86479DA0232CB1A8380024008652`).

## Seguridad y gates restantes

El codec valida el contexto de entrada esperado, pero el caller aún debe obtenerlo de membresía confiable y comprobar que el firmante tenía rol y membresía activos para esa revisión. No se implementó `RecoveryKit` Android, revocación, pairing, API ni sincronización. La prueba usa semillas/IDs/clave sintéticos; no se cargan datos de usuario. El AVD es x86_64 con Keystore software; faltan Samsung S24/ARM64, empaquetado nativo auditado, vectors Web y aceptación del flujo completo en una app Android productiva. No activa bloqueo remoto. Fase 2 sigue abierta.
