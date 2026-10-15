# Pantalla en blanco al actualizar Windows — plan de corrección

## Evidencia inicial

La UI instalada coincide por SHA-256 con el payload .163. Todos los assets locales coinciden. El smoke .163 con perfil aislado pasa. El renderer de la instancia instalada permanece en `about:blank`, antes de navegar a `focusguard.app`. Copiar solo cachés a un perfil aislado y abrir desde arranque minimizado no reproduce el bloqueo. No borrar datos del propietario para probar hipótesis.

## Causa reproducida

`DesktopSettingsStore.Load/SaveVerified` espera sincrónicamente tareas del lease de archivos. Sus awaits capturan `WindowsFormsSynchronizationContext`. Ante contención del lease o I/O asíncrono, el UI thread bloquea la continuación que él mismo necesita ejecutar. `DesktopSettingsUiThreadTests` fuerza la contención y falla por timeout antes de la corrección. La lectura de preferencias tras crear WebView2 se encuentra antes de la navegación, coincidiendo con `about:blank` observado.

## Plan y aceptación

1. Evitar capturar el contexto de UI en el helper de I/O, incluidos reintentos, lectura, escritura y disposal asíncrono.
2. Probar carga y guardado desde un contexto Windows Forms real bajo contención; verificar persistencia y conservación de preferencias.
3. Probar la ventana real con preferencias persistentes y arranque desde bandeja.
4. Generar un hotfix .164 basado en los binarios/recursos .163 ya verificados y reemplazar únicamente `FocusGuard.Client.dll`/PDB. Excluir la integración automática Host/UI de Fase 3 todavía en desarrollo. Actualizar manifiesto/distribución del paquete y recompilar Setup con el payload exacto.
5. Validar paquete, hashes y renderer publicado; conservar claves, tokens, preferencias, perfil WebView y servicio instalado.

## Seguridad y recuperación

No cambiar protocolos, membresía ni controles de bloqueo. Para diagnóstico se reinició solo la UI; el servicio permaneció Running. El puerto de diagnóstico efímero del navegador se cerrará al terminar. No registrar credenciales, datos del vault ni contenidos privados en la evidencia.
