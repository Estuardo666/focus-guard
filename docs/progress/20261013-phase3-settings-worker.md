# Preparación Fase 3: worker local de sincronización

## Plan y alcance

Preparar un ciclo caller-driven de lectura, validación, merge, preparación durable, publicación y confirmación por lectura del documento comprometido. Verificar consentimiento antes de cada nueva solicitud, vincular vault y sesión, conservar borradores/ACK perdidos y conflictos. Trabajo exclusivamente local; Fase 2 sigue abierta por la otra PC. No activar temporizadores ni modificar cloud o app instalada.

## Implementación

- SettingsSyncCryptography fija cuenta/generación/entidad/key epoch y limita la revisión de membresía. Solo abre documentos FG-E2EE-1 verificados y resuelve autores mediante callback de historia ya verificada con WriteSettings. La preparación comprueba la clave pública local autorizada y la lectura valida firma/AEAD/esquema. Posee una copia de la clave de contenido que borra al Dispose; la identidad pertenece al caller.
- SettingsSyncWorker requiere consentimiento explícito por callback y scope exacto del vault. Persiste el sobre antes de Publish. Una respuesta Stored no vacía la cola: exige lectura autenticada y coincidencia del documento comprometido. Puede recuperar una respuesta perdida sin volver a publicar y conserva ediciones posteriores.
- Bloquea rollback, revisión equívoca, documento faltante de una baseline existente y contexto/firmas incorrectos. Los conflictos existentes sobreviven polls posteriores hasta Resolve deliberado (regresión corregida en SettingsOutbox).
- No hay temporizador, autohabilitación ni ProjectReference desde los componentes distribuidos. La versión instalada .159 no cambia.

## Verificación y límites

Pruebas de worker con criptografía/DPAPI reales y un transporte controlado en memoria que simula resultados y pérdida de respuesta. La historia de membresía es sintética; estas pruebas NO verifican HTTP, OIDC, el verificador real de membresía, durabilidad del servidor ni convergencia en PCs físicas. El transporte futuro debe autenticar y comprobar account/device/proof y devolver solo documentos comprometidos. No usar avisos/push como prueba de commit.

Stored/RevisionConflict son los únicos resultados del puerto. Los errores de membresía, rekey, revocación, autenticación o protocolo deben ser errores distintos en el adaptador HTTP, nunca reinterpretarse como conflicto de revisión. La sustitución de una sesión criptográfica al cambiar membresía/época requiere reconciliar o reproteger las escrituras pendientes; aún no se implementó.

Todavía faltan HTTP/cursor/backoff, integración del verificador real de membresía, formato firmado de tombstones, export/reset, consentimiento y UI/runtime. Fase 3 no se declara terminada. Registrar pruebas, formato, diff y AST finales abajo.

Resultado final:41/41 pruebas locales PASS, repetidas tras formato; whitespace Core/Windows/Tests verificado; diff-check exit0 (avisos preexistentes LF/CRLF); AST actualizado. Verificada ausencia de referencias desde componentes distribuidos. El contexto criptografico serializa uso y Dispose de su copia de clave para impedir borrado concurrente durante cifrado/descifrado. No se ejecuto transporte real ni se modificaron datos/cuentas del propietario. Fase2 y Fase3 permanecen abiertas.
