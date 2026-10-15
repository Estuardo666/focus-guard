# Fase 3 — preparación local del núcleo de ajustes

## Alcance autorizado

Preparar código y pruebas locales para la sincronización automática. Fase 2 sigue abierta hasta la prueba pendiente con el otro Windows. No desplegar ni habilitar sincronización automática con esta preparación.

## Plan

Separar un módulo puro FocusGuard.Sync.Core: allowlist inicial tema/idioma/duración, formato validado, merge de tres vías, CAS/idempotencia, borrado y overrides locales. Probar concurrencia, entradas no admitidas, rollback y no resurrección. Mantener sin referencias desde componentes distribuidos.

## Implementación

- `cloud/sync/Core`: tres ajustes iniciales (dark/light, es/en, 1–720 minutos); payload JSON cerrado con rechazo de duplicados/campos desconocidos y límite de tamaño. Canonicalización de este esquema acotado verificada por prueba de bytes; no se implementó criptografía.
- Merge conserva borrador offline, incorpora cambios remotos en otros campos, deduplica cambios idénticos y bloquea conflictos. Un tombstone nuevo no puede resucitarse automáticamente desde una edición anterior, incluso si sus valores efectivos eran los predeterminados.
- Propuesta CAS con ID estable y ACK vinculado a operación, revisión y contenido exacto. No constituye todavía una outbox duradera ni un mecanismo de ACK de transporte.
- Overrides locales de los tres campos: solo presentación/configuración futura; no se incluyen en el documento global.
- Proyecto y pruebas incluidos en solución, sin ProjectReference desde Host/Service/API/Android ni activación en runtime.

## Validación y límites

`dotnet test cloud/sync/Tests/FocusGuard.Sync.Tests.csproj -c Release --nologo`: 17/17 PASS. Se detectaron inicialmente finales LF distintos del CRLF exigido por el repositorio; se aplicó formato solo a los proyectos nuevos y se verifica de nuevo. No cambió un componente distribuido: no se requiere nuevo instalador ni instalación. La versión instalada sigue .159.

No se sincronizan todavía ajustes reales. Faltan consentimiento/configuración, almacenamiento local cifrado de base/borrador/outbox, detección de cambios, transporte FG-E2EE-1 firmado y CAS, snapshot/cursor, reintentos, integración de overrides y UI Pendiente/conflicto/restablecer/exportar. Otros ajustes del apartado 9.1 (accesibilidad, notificaciones, etc.) requieren esquema y aceptación separados; este núcleo cubre el MVP de tres campos del criterio de Fase 3.

Seguridad: no accede a credenciales ni cuentas; entradas remotas requieren verificación de membresía/E2EE antes de llamar al núcleo; no copiar rutas, permisos, enforcement, recuperación, perfiles ni historial. Un conflicto no publica automáticamente el borrador.

Próximo tramo: base/borrador/outbox cifrada y persistencia atómica recuperable, con pruebas de reinicio y pérdida de ACK; después adaptador del documento cifrado existente. Fase 3 no se declara terminada y Fase 2 conserva la prueba física pendiente.

Resultado final: formato whitespace de ambos proyectos verificado (exit0),17/17 pruebas repetidas tras formato (exit0), solucion reconoce los dos proyectos y busqueda confirma que componentes distribuidos no los referencian. git diff --check exit0 (avisos LF/CRLF preexistentes). graphify update exit0,22653 nodos/141356 aristas. Preparacion de este nucleo completada; Fase3 funcional y Fase2 fisica siguen abiertas.

Correccion de limite de revision: snapshot acepta la ultima revision segura 9007199254740991, mientras una nueva propuesta exige base<=9007199254740990. Antes una escritura final valida no podia validarse como snapshot confirmado. Regresion prueba confirmar esa revision y rechazar incremento/desbordamiento.104 tests PASS; formato aplicado y diff-check exit0. Sin runtime, cloud o datos reales. Fase2 permanece abierta.
