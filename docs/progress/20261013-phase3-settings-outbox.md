# Preparación Fase 3: outbox local cifrada

## Alcance y plan

Continuación autorizada de preparación de código y pruebas locales. Fase 2 permanece abierta por la prueba pendiente con el otro Windows. Implementar un estado recuperable con una escritura CAS en vuelo y un borrador posterior; conservar operación y bytes exactos ante pérdida de ACK; cifrar persistencia local con DPAPI existente; probar corrupción, cuenta/generación equivocada, cancelación y concurrencia.

## Componentes

- SettingsOutbox (núcleo independiente): editar, preparar una sola escritura, reintentar sin volver a cifrar, confirmar solo operación/revisión/payload coincidentes, conservar borradores posteriores al envío, diferir snapshots mientras hay escritura en vuelo, recuperar conflicto CAS y exigir resolución deliberada.
- FocusGuard.Sync.Windows: vault DPAPI CurrentUser ligado a issuer/cuenta/generación/dispositivo. JSON versionado y cerrado, límite de tamaño, temporal único, flush al disco y reemplazo final; lecturas y actualización read-modify-write con lease exclusivo del sistema operativo. Un temporal huérfano sin commit final genera recuperación requerida; no se interpreta como outbox vacía ni se elimina silenciosamente. No hay fallback ante corrupción o scope incorrecto.
- Tests con DPAPI real: recuperación mediante nueva instancia del vault, pérdida de ACK, bytes exactos de reintento, ediciones posteriores, cancelación, corrupción, scope equivocado, falta de plaintext en archivo y dos instancias concurrentes.

## Seguridad y límites

El adaptador futuro debe verificar FG-E2EE-1/membresía antes de Receive y producir un sobre cifrado y firmado desde Prepare. El callback de prueba devuelve bytes opacos; no demuestra criptografía E2EE ni transporte real. El vault solo implementa cifrado local y no accede a los datos reales del propietario. No captura tokens ni cambia consentimiento/enforcement.

El futuro scheduler debe usar UpdateAsync y esperar el commit durable antes de enviar. SaveAsync es para estado inicial/reemplazo explícito, no para sobrescribir a ciegas el estado leído antes de adquirir el lease. El protocolo OS/FS no promete supervivencia de todo fallo de hardware; sí evita escrituras parciales visibles y mantiene el commit anterior cuando se cancela antes del reemplazo.

El módulo Windows permanece sin referencia desde Host, Service, API o Android; no requiere instalador y no habilita sincronización automática. La Fase 3 todavía necesita transporte/cursor/reintentos, worker y consentimiento, UI de estado/conflictos y reset/export explícitos. No se declara cerrada ni sustituye aceptación con dispositivos físicos.

## Validación

Ejecutar dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release, formato de los tres proyectos nuevos, diff-check y actualización AST. Registrar resultados finales abajo.

Resultado: 26/26 pruebas PASS, repetidas despues de formato; formato whitespace verificado en Core/Windows/Tests; diff-check exit0, solo avisos de finales de linea preexistentes. Confirmado que ningun componente distribuido referencia los modulos sync. No despliegue ni instalacion; version .159 conservada. AST actualizado. Fase2 y Fase3 siguen abiertas.
