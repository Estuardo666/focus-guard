# ADR 0018 — E2EE, claves y retención acotada

Fecha: 2026-10-11. Estado: diseño CLOUD-0. No hay documentos persistidos en cloud; el codec local de contenido quedó especificado después en [ADR 0022](0022-xchacha-content-documents.md).
Las credenciales PBKDF2 del recovery local actual no se migran a este protocolo.

## Decisión criptográfica

Suite FG-E2EE-1: contenido XChaCha20-Poly1305 (clave 32 bytes, nonce aleatorio 24 bytes, tag 16 bytes), firma Ed25519 (public key 32 bytes, signature 64 bytes), digest SHA-256. Transporte base64url sin padding. Generación aleatoria OS; nunca derivar clave de contenido de password.

Canonicalizar objetos firmados y AAD con RFC 8785/JCS. Strings exactas UTF-8, sin normalización Unicode implícita. Rechazar claves JSON duplicadas, unknown fields, NaN/Infinity y números fuera de enteros seguros. Revisiones/epochs ≤2^53−1; timestamps UTC RFC3339 con milisegundos y Z. IDs UUID lowercase; firma excluye solo su campo signature, con domain separation.

Aad documento: protocol, generationId, accountId, entityId, kind, baseRevision, proposedRevision, keyEpoch. proposedRevision=baseRevision+1 antes de cifrar; servidor CAS acepta esa revisión sin modificar ciphertext/AAD. Cursor de transporte fuera del documento. Firma autor cubre cabecera, nonce y ciphertext; destinatario verifica pertenencia del autor en revisión correspondiente.

## Sobres de clave interoperables

Nota posterior: la selección provisional HPKE de esta sección fue sustituida por [ADR0020](0020-signed-libsodium-key-envelopes.md) tras pruebas reales de la alternativa libsodium firmada. El texto siguiente se conserva como razonamiento histórico, no formato vigente.

No asumir que NSec implementa crypto_box_seal de libsodium. Usar formato versionado HPKE RFC 9180: DHKEM(X25519, HKDF-SHA256), HKDF-SHA256 y ChaCha20Poly1305 para key wrapping (suite IDs 0x0020/0x0001/0x0003), con proveedor probado que soporte la suite en C#/Android/Web. Binding AAD: generación, epoch, receptor, clave pública destino y digest membresía. Firma del autorizador valida origen, porque HPKE base mode no identifica emisor.

Selección de backend HPKE es gate técnico antes de escribir implementación; no crear a mano un esquema ECDH/KDF alternativo si falta interoperabilidad. Alternativa admisible: binding auditado a crypto_box_seal en todos los clientes, mediante ADR que sustituya el formato antes de datos persistidos. Claves X25519 y Ed25519 separadas.

Contenido y key wrapping usan algoritmos distintos y eso se declara en suite. NSec/libsodium.js cubren las primitivas de contenido, no se da por validado key wrapping. [HPKE RFC 9180](https://www.rfc-editor.org/rfc/rfc9180.html), [JCS RFC 8785](https://www.rfc-editor.org/rfc/rfc8785.html).

## Kit y revocación

Kit aleatorio con secreto de wrapping recovery y raíz de firma separados. Registrar claves públicas como ancla; copia cifrada del material privado solo recuperable por kit. Mostrar/exportar localmente, sin enviar secreto en claro al servidor.

Tras revocar, epoch nueva aleatoria y sobres solo a miembros vigentes, incluido recovery anchor. Si no hay miembro confiable online, negar acceso revocado y pausar publicaciones hasta rotar. Revocación, commit Strict y cambios de membresía serializados por cuenta.

Kit comprometido: transición firmada mediante miembro confiable y reautenticación, claves nuevas, invalidate envolturas futuras; contenido histórico conocido no se borra criptográficamente de copias del atacante. Sin kit/miembro: nueva generación con pérdida explícita y consentimientos nuevos.

## Retención y límites

No ZDR total: documentos cifrados, tickets/ACK, identidad y anti-replay requieren almacenamiento. Contenido privado descifrado y categorías no recopiladas tienen ausencia de retención cloud. Nunca loggear bodies, cookies, claves, tokens ni ciphertext de perfiles.

Retención de referencia: intents 7 días tras fin, dedup/tombstones 90 días, logs operativos sanitizados 14 días, seguridad mínima 30 días, backups 30 días. Challenges/request replay máximo 5 min; solicitudes de vinculación caducan a 5 min y se purgan dentro de 24 h. Session secret store nativo conserva solo lo requerido por vigencia; no mezclar con logging.

Snapshots referenciados quedan fijados hasta fin/reconcile aunque se borre perfil editable. Proteger autoridad/recovery local mientras exista Strict, aunque se borre cuenta cloud.

## Gates

Vectors de JCS, AEAD, firmas, HPKE y recuperación idénticos en C#/Kotlin/Web; publicación CAS concurrente; revocación y varias epochs; claves corruptas, nonce inválido y truncamiento rechazados. Revisar CSP, BFF, memoria/keystore y logs secundarios. No afirmar protección contra endpoint o servidor de distribución web comprometido.

Las bibliotecas y licencias seleccionadas se documentan en [plataformas](../cloud-phase0/PLATFORMS-AND-DEPENDENCIES.md). Versiones exactas se fijarán solo tras verificar paquete, ABI y avisos de seguridad; no instalar dependencias en CLOUD-0 documental.

