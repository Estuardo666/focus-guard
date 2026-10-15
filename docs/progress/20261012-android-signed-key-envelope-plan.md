# Plan — sobres FG-SODIUM-BOX-1 en Android

## Objetivo

Incorporar al probe Android el codec de sobres que entrega la clave de contenido al destinatario X25519: `crypto_box_seal`/`crypto_box_seal_open` de libsodium, envelope ligado a contexto y firma Ed25519 del autorizador.

## Contrato

- Igualar `SignedKeyEnvelopeCodec.cs`: `FG-SODIUM-BOX-1`; cuerpo JCS `FG-ACCOUNT-KEY-1`; campos exactos y base64url canónico.
- Firma: `FG-SODIUM-BOX-1` + NUL + JCS(header sin `signature`), antes de abrir el sealed-box.
- Validar contexto de confianza (cuenta/generación/epoch/destinatario/membresía/firmante), clave pública y privado del dispositivo local; no confiar en contexto del envelope.
- Android maneja sólo destinatario de dispositivo en este corte. `RecoveryKit` y recuperación de cuenta no forman parte de este probe.
- La membresía activa/rol del firmante la valida el caller/servicio antes de llamar al codec; firma válida por sí sola no concede autorización.

## Aceptación

- C#→Android y Android→C# abren la misma clave sintética con IDs/contexto y claves deterministas de ensayo.
- Rechazar envelope/suite/campo/digest/epoch/recipient/firmante/clave pública/firma/ciphertext/payload alterados, JSON duplicado/no canónico, clave que no sea32 bytes y revisiones inseguras.
- Ninguna clave de usuario, cuenta, API o permiso de red; el archivo temporal de interop contiene sólo el envelope cifrado, se elimina del AVD después.

## Secuencia

1. Extender wrapper JNA con sealed-box, y la identidad Android con apertura sólo para su clave X25519.
2. Implementar codec validado contra `SignedKeyEnvelopeCodec.cs`.
3. Añadir fixture C# firmado y roundtrip Android→C# en la Activity, con negativos y borrado temporal.
4. Construir/instalar APK sólo en el AVD autorizado, verificar ambos sentidos, permisos y artefactos.
5. Alinear ADR0020/documentación, actualizar `TASKS.md`, checkpoint y graphify. Samsung ARM64, recovery, membresía real, API y producto Android quedan como gates.
