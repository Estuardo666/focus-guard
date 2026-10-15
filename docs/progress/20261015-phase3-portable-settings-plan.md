# Fase 3 — integración de exportación e importación portátil

## Alcance y plan

Conectar `SettingsExport` con la aplicación Windows usando solo el formato versionado ya preparado: exportación explícita en JSON legible de tema, idioma y duración predeterminada; selección y confirmación nativa de campos para importar; límites de tamaño; rechazo de revisión obsoleta; preservación del resto de preferencias; y aplicación a través del coordinador existente cuando la sincronización automática tiene consentimiento.

La UI no envía rutas, contenido de archivo ni valores propuestos por el bridge. El Host abre los diálogos nativos, lee como máximo 8 KiB del archivo y vuelve a validar el preview al confirmar. La importación con sync usa `SettingsAutomaticSyncCoordinator.ImportReviewedAsync`, que compara el baseline efectivo y actualiza outbox/proyección de forma atómica. Sin sync, `DesktopSettingsStore.TryApplySharedIfUnchanged` compara los tres campos y aplica la selección bajo `DesktopSettingsFileLease`.

## Implicaciones

- Seguridad: el archivo de exportación es plaintext voluntario y contiene solo tres preferencias; no incluye identidad, llaves, rutas, reglas, permisos ni políticas. El bridge acepta únicamente nombres de acciones sin parámetros de usuario.
- UX: la tarjeta está disponible aunque no haya sesión de cuenta. Exportar y abrir/importar requieren confirmación nativa; la selección muestra valores actuales y nuevos. Inglés y español usan recursos.
- Recuperación: cancelar conserva los datos; un archivo inválido, demasiado grande o una revisión obsoleta se rechaza antes de modificar preferencias.

## Verificación

- Pruebas Host del bridge para nombres exactos de acción y rechazo de rutas suministradas por contenido web.
- Prueba de escritura local atómica para actualización seleccionada y rechazo de baseline obsoleto.
- Pruebas UI de disponibilidad local sin iniciar sesión y llamadas de export/import.
- Compilación/typecheck Host y UI; suite UI focalizada.

Sin instalación, despliegue, actualización de versión o aceptación física.
