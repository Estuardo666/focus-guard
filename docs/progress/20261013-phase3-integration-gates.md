# Fase 3: auditoría de preparación e integración pendiente

La preparación local avanzó; no equivale a sincronización funcionando en la app. Fase 2 conserva la prueba física pendiente con el otro Windows.

## Evidencia disponible

- Núcleo de tres ajustes, merge/conflicto/tombstone, outbox y proyección: suite local 92 pruebas PASS en el tramo anterior.
- Criptografía FG-E2EE-1, DPAPI y HTTPS loopback reales. Identidad, historia de membresía y servidor de almacenamiento de ese ensayo son sintéticos, no validan el sistema desplegado.
- Sin referencias desde componentes distribuidos hacia sync. No activación, instalación o despliegue.

## Hallazgo de persistencia

`DesktopSettingsStore.Save` escribe directamente el JSON y captura IOException sin devolver resultado. `Load` vuelve a defaults ante IO/JSON inválido. Ese comportamiento no puede ser evidencia de aplicación durable de una actualización remota. La integración necesita una operación acotada/atómica que informe éxito o error, preserve el documento anterior en fallo y no marque Aplicado antes de confirmar la persistencia. No modificar esa política global silenciosamente: separar la operación de sincronización y probar recuperación/concurrencia.

## Siguiente tramo ejecutable, en orden

1. Preparar un adaptador local de persistencia verificable de los tres campos: lectura fresca, preservación de otros campos, escritura temporal/flush/reemplazo, retorno explícito, lease y pruebas de fallo/edición concurrente. Probar en directorio temporal, nunca contra las preferencias del propietario.
2. Vincular contexto a identidad/vault reales y resolver firmante/WriteSettings con la historia de membresía verificada. Probar revocación/rekey/cambio de cuenta sin reutilizar clave o outbox de otra generación.
3. Resolver inicialización: un 404 del contrato actual es ambiguo; solo publicar baseline inicial tras acción explícita o respuesta autenticada que pruebe ausencia. No convertir errores en defaults.
4. Acotar la lectura upstream del sender nativo antes de habilitar runtime: IdentitySession actualmente bufferiza ResponseContentRead; el límite del parser sync no demuestra límite total de memoria.
5. Introducir consentimiento persistido, estado Pendiente/Conflicto/Atención y scheduler/cooldown durable. No activar por inicio de sesión ni importar un booleano remoto como consentimiento.
6. Validar contra API local con identidad/membresía/base de datos reales; después integrar Host/UI y generar instalador versionado con las comprobaciones requeridas. La autorización vigente es código y pruebas locales, no habilitar sincronización en cuenta o staging.
7. Snapshot/cursor y confirmación de retención de tombstones; export/reset diálogos nativos; convergencia de dos Windows como aceptación física final de Fase 3, sin declarar cerrada Fase 2 por pruebas simuladas.

## Estado

Actualización tras los tramos locales: el punto 1 tiene adaptador, coordinador, lease compartido de outbox y pruebas reales de filesystem/DPAPI, pero no está integrado con los escritores legacy de preferencias. El punto 2 tiene factory desde RestoredDeviceMaterial, pin de historia, permisos históricos y claves previas, verificados con un vault real de identidad sintética; aún falta unirlo al ciclo de cuenta/membresía/OIDC real y aplicar revocación/rekey en runtime. Los puntos 3–7 continúan pendientes. La suite de preparación local tiene 107 casos PASS; esa cifra no prueba aceptación funcional de Fase 3.

El siguiente cambio funcional debe cubrir el punto 3 (inicialización explícita con CAS, lectura posterior verificada y recuperación de resultado perdido) o el punto 5 (consentimiento durable), antes de habilitar el Host. Mantener el contrato 404 ambiguo como error hasta que haya una acción explícita o ausencia autenticada. No desplegar ni activar con esta preparación.

No hay bloqueo para seguir con preparación local. El objetivo amplio no se marca completo: pruebas físicas y requisitos funcionales pendientes no están probados por los tests del módulo. No sustituir integración real por aumentar conteos de casos aislados.

Actualización 2026-10-14: CAS0 inicial, consentimiento local y scheduler caller-owned ya tienen preparación/checkpoints propios. `SettingsRetryCheckpointStore` añade DPAPI scope-bound para persistir backoff/Retry-After entre procesos (129/129 pruebas sync). Todavía no se conecta al consent gate ni al Host, no hay cursor, y los puntos de identidad/OIDC/DB reales, UI/export/reset y convergencia siguen pendientes. Este módulo local no habilita runtime ni reduce el gate físico de Fase 2.
