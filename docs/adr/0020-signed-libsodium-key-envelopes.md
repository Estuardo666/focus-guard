# ADR 0020 — Sobres de clave libsodium firmados

Fecha: 2026-10-11. Estado: decisión de diseño sustentada por prototipos, no release de producción.
Sustituye exclusivamente la selección HPKE provisional de ADR0018. Mantiene AEAD de contenido, firmas, recovery, membresía y retención. Aún no existe contenido cloud persistido que migrar.

## Decisión y razón

Elegir FG-SODIUM-BOX-1: libsodium crypto_box_seal/crypto_box_seal_open para envolver claves de cuenta a la clave X25519 de un destinatario, con firma Ed25519 externa del autorizador. La construcción estándar de libsodium evita depender de un proveedor HPKE .NET no verificado y usa las bibliotecas de contenido ya probadas. No implementar KEM/KDF/cipher propios.

Suite de contenido FG-E2EE-1 sigue XChaCha20-Poly1305/Ed25519. El envoltorio sealed-box usa la construcción documentada X25519/XSalsa20-Poly1305/BLAKE2b de libsodium; no se etiqueta como HPKE ni XChaCha20. Claves de firma y cifrado separadas, sin conversión implícita entre ellas.

Header exacto: suite, generationId, keyEpoch, recipientId, recipientPublicKey, membershipDigest, ciphertext, signerDeviceId, signature. No encapsulatedKey separado: ya pertenece al formato sealed-box. Body descifrado JCS: protocol FG-ACCOUNT-KEY-1, accountId, generationId, keyEpoch, recipientId, membershipDigest, contentKey (32 bytes base64url sin padding).

Firmar domain FG-SODIUM-BOX-1 + byte cero + JCS(header sin signature). Validar autor activo/rol adecuado desde membresía anclada antes de descifrar; sealed-box por sí sola no autentica emisor. Comparar header y payload contra contexto vigente cuenta/generación/epoch/destinatario/membresía; no tomar ese contexto del envelope no validado. Rechazar unknown fields, JSON no canónico/UTF8 inválido/duplicados, ciphertext truncado, clave distinta de32 bytes y firma distinta de64.

## Evidencia y alcance

NSec26.4.0 con libsodium1.0.22 suministra primitivas de contenido; binding nativo de ensayo a la API C oficial prueba sealed-box C#/Node en ambos sentidos. libsodium-wrappers0.8.4 y canonicalize5.1.0 implementan codec de ensayo firmado; pruebas cubren contexto, firma, recipient/author erróneos, epoch y payload canónico. No se concluye que NSec exponga una API managed sealed-box.

El producto necesitará módulo interop pequeño revisado, ABI/RID fijos, búsqueda de DLL segura en deployment protegido y control de buffers/lifetime, o binding mantenido equivalente. Es adaptación de API oficial, no criptografía casera. Licencias MIT/ISC/Apache2 con notices; no elegir licencia raíz de producto ni afirmar compatibilidad transitive auditada por este ADR.

Android: el probe JNA/libsodium1.0.22 del AVD Android15/x86_64 ahora abre y crea sobres `FG-SODIUM-BOX-1` interoperables con C# en ambas direcciones; firmas, sealed-box y binding de epoch/generación/destinatario/membresía pasan vectores sintéticos. Esto no prueba S24/ARM64 ni empaquetado productivo. La Fase2 no habilita sincronización con claves hasta pasar Web, recovery y revocación y validar el caller contra membresía confiable. La selección de formato sí queda cerrada en Fase0; el gate Android físico/producción sigue abierto.

Auditoría Android posterior: Lazysodium5.2.0 POM declara MPL2 y JNA5.17.0/core-ktx1.16.0/Kotlin2.1.21. Su AAR verificado contiene cuatro ABI y cadena1.0.20; no aprobar ese bundle nativo sin revisión de parche/actualización. Baseline candidata del motor: libsodium1.0.22 oficial ISC, también con binarios Android observados en assets de la dependencia NSec. No consumir directamente su paquete NuGet interno como API Android ni copiar a producción sin revisar procedencia, page alignment, carga/JNI y runtime. Las notas oficiales posteriores a1.0.20 incluyen un fix de validación de puntos Ed25519; la cadena de versión no prueba por sí sola ausencia/presencia del parche. Gate cerrado por revisión pendiente, no vulnerabilidad explotable inventada.

## Fuentes

[Sealed boxes oficial](https://doc.libsodium.org/public-key_cryptography/sealed_boxes), [bindings oficiales](https://doc.libsodium.org/bindings_for_other_languages), [NSec](https://nsec.rocks/), [libsodium.js](https://github.com/jedisct1/libsodium.js), [JCS RFC8785](https://www.rfc-editor.org/rfc/rfc8785.html).
