# Fase 3 preparación local: export/import explícitos

## Alcance y plan

Preparar formato portátil limitado a los tres ajustes MVP, preview con selección, confirmación y protección de edición concurrente. No escribir archivos del propietario, transmitir datos ni activar sincronización. Fase 2 permanece abierta por la otra PC.

SettingsExport produce bytes JSON de format focusguard.settings.export/version1/settings, sin IDs de cuenta/dispositivo, rutas, permisos, enforcement, claves ni historial. Es export explícito en plaintext de solo tres preferencias, no backup de credenciales, recuperación ni outbox.

Preview rechaza campos desconocidos, duplicados, ausencia de campos, valores inválidos, tamaño >8KiB y versiones desconocidas. Conserva campos no seleccionados. Confirm requiere acción deliberada, baseline actual idéntica y propuesta consistente con la selección; no aplica ajustes ni encola/publica una escritura. El caller futuro debe obtener confirmación mediante diálogo nativo confiable, no aceptar un booleano remitido por contenido remoto como prueba de autorización.

Pruebas locales de formato exacto/allowlist, confirmación, selección, preview obsoleto, entradas no permitidas y versión. No prueban UI, diálogos, escritura de archivo, aplicación de DesktopSettings ni sincronización real. Esas integraciones siguen pendientes junto a cursor/membresía/OIDC/DB/cooldown durable. No componente distribuido modificado; no instalador o despliegue requerido. Registrar resultado final abajo.

Resultado final:83/83 pruebas locales PASS; formato Core/Tests verificado, diff-check exit0 con avisos LF/CRLF preexistentes, AST actualizado. Verificada ausencia de referencias desde componentes distribuidos. No se escribieron exports del propietario ni se aplicaron/importaron ajustes reales. Fase2 y Fase3 siguen abiertas.

Revision adicional:85/85 pruebas PASS. Nuevas regresiones comprueban que una preview forjada no aplica campos no seleccionados ni flags desconocidos, y que un export no puede importar tombstone o identidad de cuenta. Sin cambios de runtime o datos reales. Fase2 conserva aceptacion fisica pendiente; preparacion Fase3 sigue abierta.

Validacion posterior: guardas explicitas para Baseline/Proposed nulos o invalidos antes de aplicar la preview; nueva regresion. Grafo previo confirmado completado exit0. Prueba fisica Fase2 aun pendiente; no activar runtime.

Suite final de este tramo:86/86 PASS; diff-check exit0, formato aplicado solo a proyectos sync. Sin cambio de instalador, cloud ni datos reales.
