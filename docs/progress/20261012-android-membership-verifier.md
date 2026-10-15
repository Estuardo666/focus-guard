# Checkpoint — cadena de membresía y permisos Android

## Resultado

Android ahora valida una cadena firmada `FG-MEMBERSHIP-1` compatible con `MembershipProtocol` de C#: ancla inicial, firmas Ed25519, digest JCS/SHA-256, continuidad de revisión, generación/cuenta, epoch, roles/status y claves públicas estables. Para crear `VerifiedMembership`, el caller debe pasar accountId/generationId del contexto autenticado; el verificador rechaza que la cadena se presente a otra cuenta.

Las APIs públicas de apertura de sobres y documentos ahora requieren esa membresía validada. Se comprueba que la clave del sobre corresponde al destinatario pendiente/activo, que el firmante esté Active con `ManageDevices`, y que el autor de documentos esté Active con `Read`/`WriteSettings` en la revisión y epoch firmados. Un dispositivo Pending puede abrir sólo el sobre destinado a su clave para completar el alta; no puede leer documentos.

## Archivos cambiados

- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidMembershipVerifier.java`: modelos Android, parser estricto, firma/digest, ancla/cadena y `VerifiedMembership` con autorizaciones.
- `prototypes/cloud-phase0/android/src/app/focusguard/phase0/AndroidDeviceIdentity.java`: importación/validación de identidad pública y fingerprint para claves de miembros.
- `AndroidSignedKeyEnvelopeCodec.java`: las entradas públicas requieren el objeto de membresía validado antes de crear/abrir sobres.
- `AndroidContentDocumentCodec.java`: las entradas públicas requieren membresía validada antes de cifrar/descifrar documentos.
- `AndroidMembershipRuntimeProbe.java` y `ProbeActivity.java`: vector C# firmado, envelope y documento con permisos efectivos.
- `prototypes/cloud-phase0/DeviceInterop/Program.cs`: genera un fixture de C# con genesis, revisión2, sobre/datos cifrados y una transición firmada por un emisor que era Pending.
- `android/build-probe.ps1`, `TASKS.md`, ADR/README y este checkpoint.

## Validación

- Java17 `-Xlint:deprecation -Werror`, D8 minSdk26, APK local v2/v3: PASS.
- AVD Android15/API35, solo synthetic: C#→Android verifica genesis/revisión y digest exacto; envelope firmado sólo se abre con el `VerifiedMembership`; documento de C# se descifra con el overload que exige membership: PASS.
- El firmante de revisión2 fue activo `ManageDevices` en revisión1; el destinatario figura Pending, puede confirmar el sobre, pero `Read`/documento se rechaza. El autor activo con Read/WriteSettings pasa.
- Rechazos: digest/cuota de membresía, revisión/epoch, cadena sin genesis, otra cuenta, campo desconocido, firma manipulada y transición firmada por un dispositivo que estaba Pending: PASS.
- Después de ese build final, C# `DeviceInterop` volvió a abrir los envelopes de documento y de clave creados por Android: ambos PASS.
- APK solicita sólo `PACKAGE_USAGE_STATS`; sin `INTERNET`. Los archivos de ciphertext temporales se borraron del sandbox AVD después de verificación.

## Artefactos

- APK combinado: `artifacts/cloud-phase0/android-identity-probe/focusguard-phase0-test.apk`.
- SHA-256 APK: `6BA11A964985238C0BFF85E689F0B7DD10913B77BCD70CA366E4B42546592169`.
- UI XML: `artifacts/cloud-phase0/android-identity-probe/membership-final.xml`.
- Captura: `artifacts/cloud-phase0/android-identity-probe/android-membership-pass.png` (SHA-256 `E7256FC1BFA257A1ECFBE33A9934BA9478E42CD08EB70AFF669FB0F2465A6AE3`).

## Límites

La cadena de membresía debe llegar por un flujo autenticado; accountId/generationId esperados no se deben leer del mismo JSON no confiable. El harness no implementa OIDC móvil, transporte TLS/API, bootstrap real, recuperación, revocación en nube ni validación Web. La cadena genesis necesita el vínculo confiable de cuenta/bootstrap del servidor. La verificación local no activa ni autoriza bloqueo remoto. AVD x86_64/Keystore software; falta Samsung ARM64 y producto Android real.
