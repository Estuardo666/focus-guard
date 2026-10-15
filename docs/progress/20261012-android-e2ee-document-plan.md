# Plan — codec Android FG-E2EE-1

## Objetivo

Implementar en el probe Android el formato de documentos firmado/cifrado usado por `cloud/devices/Core/ContentDocumentCodec.cs`, sin cuenta real ni transporte. La prueba debe abrir un documento C# en Android y permitir que C# abra uno creado en Android, con datos sintéticos.

## Contrato fijado para este corte

- Seguir byte por byte el codec C# actual: XChaCha20-Poly1305-IETF, clave32, nonce24, etiqueta16; plaintext objeto JSON JCS, UTF-8 estricto y límite256KiB/profundidad32.
- La AAD JCS incluye `protocol`, cuenta, generación, entidad, tipo, revisión base/propuesta, epoch y `membershipRevision`, igual al código C# actual.
- La cabecera firmada JCS incluye el suite, el contexto completo, autor, nonce y ciphertext; excluye sólo `signature`. Firma Ed25519 con dominio `FG-DOCUMENT-1` + NUL.
- Corregir ADR0022 para documentar `membershipRevision` en AAD; es una precisión del contrato ya implementado en C#, no un cambio del formato C#.
- El autor de prueba usa semillas/IDs fijos de ensayo compartidos con C#; nunca claves personales. El contenido/clave son sintéticos y no salen del AVD salvo el ciphertext de prueba firmado para la verificación local C#.

## Criterios de aceptación

- Rechazar contexto, suite, revisiones, dominio, autor, codificación, etiqueta AEAD, firma, texto no canónico, UTF-8 inválido, JSON no objeto, claves duplicadas y documento demasiado grande/profundo.
- C#→Android y Android→C# abren exactamente los mismos bytes canónicos con el contrato y los vectores existentes; los fallos de integridad no devuelven texto.
- El APK continúa sin `INTERNET`, y no incluye ni instala cliente productivo, pairing, permiso adicional o bloqueo.
- Construir/instalar sólo en el AVD ya autorizado; dejar evidencia, actualizar TASKS y graphify.

## Riesgos y recuperación

La escritura previa a nube no existe en este corte. Un fallo de firma o AEAD debe fallar cerrado. El fichero de interoperabilidad generado en el AVD contiene sólo envelope cifrado de ensayo y se elimina después de verificarlo en C#. El prototipo nativo x86_64/AVD no demuestra Samsung/ARM64 ni endurecimiento de la carga nativa para producción.

## Secuencia

1. Alinear ADR0022 con el AAD que el codec C# ya genera.
2. Añadir primitivas al wrapper JNA Android y construir el codec Android con validación estricta.
3. Extender el probe con vector C#→Android, roundtrip y rechazos criptográficos; guardar sólo el envelope Android sintético para extraerlo localmente.
4. Añadir el modo Android→C# al harness existente y verificar ambos sentidos; reconstruir/instalar APK y revisar permisos/UI.
5. Registrar resultados, hashes, límites y siguiente gate; regenerar graphify.
