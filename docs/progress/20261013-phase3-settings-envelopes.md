# Fase 3 preparación local: sobres cifrados de ajustes

## Plan y alcance

Conectar la outbox de preparación al ContentDocumentCodec FG-E2EE-1 existente mediante un adaptador acotado. Reutilizar XChaCha20-Poly1305/Ed25519, sin diseñar primitivas ni modificar los componentes distribuidos. Probar roundtrip, persistencia DPAPI, bytes de reintento, contexto/signatura/clave/ciphertext y esquema no permitido. Fase 2 sigue abierta por la otra PC; no habilitar sincronización ni desplegar cloud.

## Resultado de implementación

SettingsEnvelopeCodec enlaza la propuesta CAS con Kind settings.global.v1, base y proposed revision antes de cifrar/firmar. Abre solamente sobres de tamaño acotado y esquema cerrado, rechaza claves JSON duplicadas, valida contexto/AEAD/firma mediante el codec existente y valida los tres ajustes después de descifrar. El buffer de plaintext se borra en finally. El firmante debe venir de la historia de membresía verificada: este adaptador no reemplaza ese verificador.

Las pruebas crean identidades y claves sintéticas; ejecutan las primitivas reales, guardan la outbox con DPAPI y recuperan los bytes mediante otra instancia del vault. No prueban dos PCs físicas, transporte HTTP, convergencia en cloud ni obtención real de claves de la cuenta del propietario. Un firmante con clave válida tampoco puede añadir permisos al payload permitido.

La dependencia añadida va del módulo nuevo hacia FocusGuard.Devices; ningún componente distribuido referencia sync. No se requiere nuevo instalador. La versión instalada .159 y los datos del propietario permanecen sin cambios.

Próximo paso: worker de publicación/recepción con consentimiento, recuperación de ACK perdido comprobando ciphertext, adaptación HTTP CAS y cursor; validar primero con servidor local. Export/reset/UI y aceptación física siguen pendientes. Registrar resultados de pruebas, formato y AST al finalizar.

El adaptador actual cubre documentos normales; la señal de tombstone del núcleo todavía requiere formato cifrado/firmado y adaptación de intención/ACK. No se debe interpretar una marca de borrado del servidor como operación verificada ni afirmar que el borrado E2EE está implementado.

Recuperacion adicional de ACK perdido: ConfirmOwnWrite solo confirma un eco con firma/AEAD/contexto verificados y documento canonico identico al cifrado preparado. Otro cifrado valido con los mismos ajustes no vacia la cola; una edicion posterior se conserva. No usa coincidencia de valores como prueba de commit ni declara un commit remoto sin leerlo del transporte futuro.

Validacion final:32/32 pruebas PASS con libsodium/Ed25519 y DPAPI reales en identidades sinteticas; formato Core/Tests verificado sin cambios; diff-check exit0 y ausencia de referencias desde componentes distribuidos confirmada. La confirmacion perdida exige que el futuro transporte obtenga el eco de una lectura autenticada del documento comprometido: una firma valida acredita autor/contenido, no persistencia cloud por si sola. No se activa ningun worker ni se modifica staging. Fase2 y Fase3 permanecen abiertas.

PinnedSettingsAuthor añadido: valida snapshot firmado contra account/generation/revision/digest previamente confiado localmente; copia profunda antes de pinning, Active/WriteSettings exigidos. No establece nueva confianza ni verifica transiciones historicas: revision distinta se rechaza. Regresion firma real/digest equivocado/signer desconocido y mutacion de arrays posteriores;105 pruebas PASS. Todavia no conectado al vault real ni al worker del Host. No cloud ni datos reales.

Historia de autores preparada: cadena completa desde revision1 hasta digest local previamente confiado, firmas/cuenta/generacion/secuencia/PreviousDigest y ManageDevices del firmante antes/despues; claves retenidas inmutables, epoch no decreciente. Resolve exige Active/WriteSettings en revision del documento, permite lectura historica previa a retirada del permiso sin autorizar revision posterior. Pruebas firman dos revisiones reales y rechazan cadena incompleta/enlace roto.105 casos PASS con nuevas aserciones. No aprende confianza del servidor ni avanza el pin automaticamente; no sustituye politica de operaciones de membresia o permisos de lector actuales ni seleccion de claves historicas. Host aun no conectado.

Claves historicas preparadas: SettingsSyncCryptography admite hasta64 claves anteriores autorizadas aportadas por caller, valida epoch/tamaño, copia defensiva y limpia todas al Dispose bajo lock. Open/Confirm eligen clave por epoch firmado; Prepare siempre usa epoch actual. Test cifra documento antiguo, borra buffer original, verifica lectura con copia y nueva escritura epoch actual; sin history rechaza.106 pruebas PASS. Caller aun debe aportar claves desde vault/contexto confiado y validar permiso Read actual; no aprende claves del servidor ni usa key anterior para nuevas escrituras. Host no conectado.

SettingsSyncContextFactory preparada desde RestoredDeviceMaterial: cadena contra revision/digest confiado del vault, Active/Read actual, identidad exacta, epoch y sesion presentes; claves historicas disponibles tomadas por epoch de historia y copias temporales limpiadas. No expone nueva exportacion de claves ni modifica vault. Caller mantiene material vivo durante uso de crypto y verifica OIDC cuenta antes de transporte; factory no establece esa vinculación por si sola. Test vault DPAPI real con identidad sintetica y roundtrip, rechazo otra cuenta;107 pruebas PASS. No acceso al vault real ni runtime/cloud.

Factory defensiva: copia completa de historia antes de validar/usar campos; rechazo adicional probado para vault sin deviceSession y epoch distinta a membresia confiada. Fixture usa allowReplace explicito solo para sus propios vaults temporales, tras fallo inicial por proteccion create-only existente.107 pruebas PASS; no relajar proteccion del vault productivo. No datos reales ni runtime.
