# Fase 3 preparación local: reset/tombstone autenticado

## Plan y alcance

Codificar una eliminación lógica cifrada/firmada, conservarla en outbox/vault, enlazarla al ACK y prevenir resurrección por ediciones offline. Acción exclusivamente local de preparación; no borrar documentos ni preferencias reales y mantener Fase 2 abierta.

## Implementación

SettingsWriteIntent incorpora Deleted y exige valores canónicos predeterminados para reset. Outbox incorpora DraftDeleted y RequestReset(ownerConfirmed): sin confirmación deliberada se rechaza. Un reset posterior a una escritura en vuelo queda pendiente tras el ACK anterior. Un cambio remoto concurrente con reset no se fusiona: exige resolución/reconfirmación deliberada.

El payload cifrado de borrado es exactamente {"deleted":true}, dentro de FG-E2EE-1 settings.global.v1 y su firma/contexto CAS. No se admite deleted=false, número, campos mixtos ni un flag externo no autenticado. La decodificación devuelve snapshot Deleted=true y defaults; ConfirmOwnWrite enlaza tipo de operación, ID, revisión, valores y documento cifrado exacto. Un ACK de documento normal con defaults no confirma una eliminación.

Se preserva el tombstone como revisión nueva, no como ausencia HTTP o eliminación física. Lectores antiguos que solo entienden el esquema normal rechazan el payload; no se afirma compatibilidad de runtime ya distribuido. Los actores futuros necesitan la nueva decodificación y una señal de actualización requerida apropiada.

## Evidencia y límites

Tests locales con firmas/AEAD/DPAPI reales: confirmación requerida, ACK equivocado, orden de reset tras publicación, conflicto CAS de reset, restart de vault, payload inválido y bloqueo de edición offline anterior. El fixture HTTPS local también ejecuta reset revisión3 y readback firmado, conservando en servidor solo ciphertext. Auth/membresía y almacenamiento server son sintéticos/in-memory, no OIDC/Neon ni aceptación física.

No se modificó backend desplegado, app ni documentos reales. No hay UI/reset/export conectado ni política de retención/GC de tombstones. Una edición nueva después de conocer un tombstone puede crear una revisión nueva deliberadamente; una edición antigua no lo recrea automáticamente. Overrides/permisos/enforcement locales quedan fuera del documento.

Fase 2 y Fase 3 siguen abiertas. Registrar formato/tests/diff/AST finales abajo.

Resultado final:76/76 pruebas locales PASS, incluidos crypto/DPAPI y HTTPS real con autoridad/almacenamiento sinteticos. Formato Core/Tests verificado, diff-check exit0 con avisos preexistentes LF/CRLF, AST actualizado. Confirmada ausencia de referencias desde componentes distribuidos. No se borro ni restablecio ningun ajuste real del propietario. Fase2 y Fase3 siguen abiertas.
