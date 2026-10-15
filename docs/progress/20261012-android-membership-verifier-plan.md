# Plan — verificador Android de membresía y autoridad

## Objetivo

Crear una cadena `FG-MEMBERSHIP-1` verificable en Android para que el caller pueda relacionar el digest del sobre de clave con una membresía firmada, comprobar que el autorizador estaba activo con `ManageDevices` y limitar lectura de documentos a autores activos con `Read`.

## Contrato y límites

- Igualar `MembershipProtocol.CanonicalMembership`, firma `FG-MEMBERSHIP-1` + NUL, digest SHA-256 con el mismo dominio, validación de UUID/roles/keys/status/revisiones y ancla inicial.
- Aceptar una cadena contigua genesis→snapshot actual; enlazar `previousDigest`, account/generation y revisiones.
- El firmante de cada revisión posterior debe ser `Active` + `ManageDevices` en la revisión anterior; el snapshot actual es firmado y su signer se mantiene activo.
- Autorizar envelope sólo si cuenta/generación/epoch/digest/destinatario concuerdan; un destinatario `Pending` puede confirmar su vínculo, pero no leer documentos.
- Autorizar documento sólo con membership revision/epoch actuales y autor `Active` + `Read` cuyas claves coincidan.
- La API debe entregar la cadena/contexto confiables; esto no autentica por sí solo el canal ni concede permisos locales de bloqueo.

## Aceptación

- Una cadena sintética emitida y firmada por el harness C# valida igual en Android; digest coincide byte a byte.
- Un envelope C# sólo abre por el overload que recibe `VerifiedMembership`; rechazo si digest/contexto no coincide.
- Rechazar digest anterior/alterado, saltos, firma inválida, genesis inválida, signer no activo/sin rol, roles/status/claves desconocidos, cadena cruzada de cuenta/generación y snapshot no continuo.
- Validar autorización de documento: `Read`+Active pasa; pending/revoked/falta de rol o revision/epoch diferente se rechazan.

## Secuencia

1. Añadir tipos/codec Android para parsear miembros y canonicalizar exactamente como C#.
2. Verificar genesis, firmas, digest, continuidad y autoridad del firmante; crear objeto `VerifiedMembership` no construible fuera del verificador.
3. Añadir overloads de apertura de sobres y documentos que exigen ese objeto.
4. Generar chain C# + envelope/document fixtures; ejecutar negativos Android en AVD y C# interop donde aplique.
5. Actualizar ADR/docs/TASKS y graphify. Aún faltarán API, transporte autenticado, C#/Android/Web en producción, recovery e integración UI.
