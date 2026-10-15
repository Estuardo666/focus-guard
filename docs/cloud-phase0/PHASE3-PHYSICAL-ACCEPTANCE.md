# Fase 3: aceptación física de sincronización automática

**Estado:** pendiente de ejecutar en dos PCs Windows. Esta guía prepara la prueba; las ejecuciones locales de Host, API y PostgreSQL no sustituyen la aceptación física ni la validación contra staging.

## Paquete candidato

- Setup autónomo: `artifacts/installer/FocusGuard-0.1.181-Setup.exe`
- SHA-256 del Setup: `E1932EEFEED0B9EA285511117C8CF7C93C5F35D1D0EA9C85AB616B18C3FDB065`
- ZIP del menú: `artifacts/installer/FocusGuard-0.1.181-win-x64-menu.zip`
- SHA-256 del ZIP: `8D2BB88E5189CA78EDE74DF375A54FF2F68950DEEFA8531959BA61354A566CE6`
- Verificación actual: build de producción, guard de cliente, `scripts/test-installer.ps1` y `--package-self-test` pasan; los hashes se calcularon localmente. `.181` muestra qué overrides locales siguen activos al revisar un conflicto y conserva la explicación del límite del reset introducida en `.180`, la importación portátil de `.179` y la extensión MV3 de la misma versión. No se ha instalado. La prueba en dos PCs sigue pendiente.

El cursor es opcional mientras el backend no haya sido actualizado. La aceptación física puede validar convergencia automática contra el API actual; no debe registrar como fallo que el servidor aún no devuelva `X-FG-Cursor`. La verificación del cursor contra un backend desplegado queda después del gate físico.

Antes de usarlo en cada PC, comparar el hash del Setup y ZIP con los SHA-256 indicados y anotar la versión observada después de la actualización. Instalar el mismo `.181` en ambas. Mantener la misma cuenta y los vínculos existentes; no cerrar sesión, desvincular ni volver a emparejar.

Para comparar los archivos en cada PC, abre PowerShell en la carpeta donde copiaste el paquete y ejecuta:

```powershell
Get-FileHash .\FocusGuard-0.1.181-Setup.exe -Algorithm SHA256
Get-FileHash .\FocusGuard-0.1.181-win-x64-menu.zip -Algorithm SHA256
```

Los dos valores deben coincidir exactamente con los SHA-256 de arriba antes de instalar.

## Preparar las dos PCs

1. Tener ambas PCs físicamente disponibles. La PC del propietario será el origen inicial; la PC del hermano será el receptor.
2. En ambas, confirmar la misma cuenta, correo verificado y estado **Linked to this account**. Confirmar que no haya solicitudes pendientes ni una sesión de enfoque activa.
3. Anotar los valores iniciales de tema, idioma y duración predeterminada, además de una referencia de reglas, perfiles, permisos, historial y preferencias locales como `ReduceMotion` y notificaciones.
4. Para el ensayo de convergencia inicial, dejar tema, idioma y duración iguales en ambas PCs antes de activar el consentimiento. Anotar los valores y evitar la tarjeta manual **Share/Import selected settings** durante todo este ensayo.

## Activar consentimiento y convergencia normal

1. En cada PC, abrir **Connection / Conexión → Automatic settings sync / Sincronización automática de ajustes → Enable sync / Activar sincronización** y aceptar el diálogo nativo por separado. Confirmar que ambas muestran «Sync is on for theme, language, and default timer duration…» / «La sincronización está activa para tema, idioma y duración predeterminada…».
2. En la PC del propietario, cambiar solo el tema. Mantener FocusGuard abierto en ambos equipos; esperar hasta 45 segundos y comprobar que el tema cambia en el hermano sin usar la transferencia manual.
3. Repetir por separado con idioma y duración predeterminada. En cada paso, esperar a que la sincronización se estabilice antes del siguiente.
4. Comparar las reglas, perfiles, permisos, historial, enforcement, movimiento reducido y notificaciones con los valores anotados. Estos valores deben permanecer locales y distintos donde ya lo eran.

## Cierre, reapertura y reconexión

1. Cerrar FocusGuard normalmente en ambas PCs, sin usar **Sign out**. Volver a abrirlo y confirmar que la cuenta sigue conectada, el dispositivo sigue vinculado y el consentimiento de sync permanece activo sin volver a aceptarlo.
2. Cambiar un ajuste compartido en el propietario y comprobar que llega al hermano. Si aparece «Your local settings are saved and waiting to sync.» / «Los ajustes locales se guardaron y esperan sincronizarse.», dejar ambos equipos conectados y esperar los reintentos automáticos; el backoff local puede llegar a cinco minutos y el servidor puede pedir un plazo mayor. Windows avisa al Host cuando detecta disponibilidad/cambio de dirección de red; esa señal puede despertar la cola, pero nunca acorta un `Retry-After` guardado.
3. Para una prueba de reconexión offline, desconectar Wi-Fi mediante los controles normales de Windows en ambas PCs. Cambiar la duración a valores distintos (por ejemplo, 40 y 60 minutos), uno en cada PC, y confirmar que cada valor quedó guardado localmente como pendiente. Como variante de overrides, antes de desconectar deja tema/idioma como excepciones locales distintas en ambos equipos y mantenlas durante el conflicto. Reconectar ambas y esperar la tarjeta de revisión («Compare the conflicting values and choose which one to keep for each setting.» / «Compara los valores en conflicto y elige cuál conservar en cada ajuste.»). La notificación de red despierta el scheduler cuando Windows detecta la reconexión; si hay backoff/`Retry-After`, esperar a que venza. Elegir explícitamente el valor que se conservará; comprobar que convergen las duraciones, cada tema/idioma local se mantiene y, al retirar los overrides, se aplica el valor global compartido sin otra publicación.

## Reset compartido y conflicto de tombstone

1. Cuando la sincronización esté estable, pulsar **Reset shared settings… / Restablecer ajustes compartidos…** en el propietario. Revisar en el diálogo nativo los valores actuales y el destino antes de aceptar. Confirmar solo si el preview indica los tres campos esperados.
2. Esperar a que el hermano reciba el tombstone y aplique los valores predeterminados. Comprobar que no cambia ninguna sesión de enfoque activa ni los datos locales anotados.
3. Antes del conflicto de reset, volver a crear un documento compartido activo: en el propietario cambia el idioma a **English** con ambas PCs online y espera a que el hermano también lo muestre. Confirma que sync esté estable y no haya Pending/Conflict. Sin documento activo, el botón de reset se oculta y no se puede generar un nuevo tombstone.
4. Para cubrir precedencia local en este mismo conflicto, en el propietario activa overrides locales para tema, idioma y duración (por ejemplo, `dark/es/90`) y comprueba que el hermano conserva los valores compartidos. Anota esos valores y deja activos los overrides hasta el paso 7. Si también quieres ejecutar el flujo básico sin overrides, hazlo primero y registra ambos ensayos por separado.
5. Desconecta solo el Wi‑Fi del propietario. Allí solicita y confirma **Restablecer ajustes compartidos…** mientras sync está estable, y déjalo offline. En el hermano, cambia el idioma de **English** a **Español**; espera a que se sincronice en línea y comprueba que el propietario siga offline con su reset pendiente.
6. Reconecta el propietario y espera la revisión que explica que esta PC solicitó un reset mientras otro dispositivo guardó valores nuevos («This PC requested a shared-settings reset while another device saved new values…» / «Esta PC solicitó restablecer los ajustes compartidos mientras otro dispositivo guardó valores nuevos…»). El backoff/Retry-After puede retrasar la revisión.
7. Elegir **Keep this PC’s reset / Mantener el reset de esta PC** y aplicar. Comprobar que se publica sobre la revisión nueva y que el hermano recibe el tombstone final. Los valores compartidos son defaults. Sin overrides, ambas interfaces los muestran; con overrides activos, el propietario conserva los valores locales anotados y el hermano muestra defaults. Luego, quitar los overrides del propietario y comprobar que adopta los defaults sin otro POST ni nueva revisión. Reglas, perfiles, permisos, historial y preferencias locales deben seguir iguales a la referencia.

## Cerrar la prueba y restaurar

Antes de cerrar, ejecutar también la aceptación de las integraciones y aclaraciones de UX incluidas en `.181`:

1. Con sync estable, abrir el diálogo de ajustes exclusivos de esta PC y marcar solo tema. Cambiar el tema local; comprobar que el otro equipo conserva su tema compartido. Cambiar idioma/duración y verificar que continúan sincronizándose. Anotar la selección local.
2. Cerrar y reabrir: la excepción de tema debe conservarse. Quitarla y comprobar que esta PC vuelve al último tema compartido sin publicarlo como un cambio nuevo. Cancelar el diálogo debe conservar la selección previa.
3. Exportar un archivo desde la tarjeta de ajustes portátiles. Revisar que contiene exclusivamente tema, idioma y duración, sin credenciales, identidad, políticas ni rutas. Importarlo en el otro equipo seleccionando solo un campo; comprobar que los otros dos y las preferencias locales se conservan. Si sync está activa, la importación del campo compartido debe converger automáticamente.
4. Repetir la importación sobre un campo con excepción local: debe actualizar solo ese valor en esa PC. El resto de dispositivos conserva el valor global.
5. En el receptor, conserva una excepción local activa para un campo y desactiva sync. Importa desde el archivo únicamente ese campo; confirma que el nuevo valor local se aplica, el override existente se actualiza, el consentimiento sigue apagado y el otro equipo no recibe el valor. Al reactivar sync, el valor local importado debe seguir conservándose; los otros campos y reglas/perfiles/permisos permanecen intactos.

Registrar estos resultados por separado de la transferencia manual E2EE y de la aceptación ya completada en Fase 2.

1. Anotar primero las revisiones, valores finales, resolución de ambos conflictos y el estado de consentimiento. No reescribir el registro de aceptación después de cambiar los valores.
2. Si ambos equipos tenían los mismos valores compartidos antes del ensayo, elegir los valores deseados al final en una sola PC y dejar que sincronicen.
3. Si cada PC tenía valores compartidos distintos y se quieren restaurar exactamente, desactivar la sincronización en ambas antes de editar localmente esos tres valores. La revocación conserva los valores locales y la copia cloud existente; dejar el sync desactivado hasta decidir cómo reconciliar esas copias.
4. Volver a comparar reglas, perfiles, permisos, historial, enforcement, movimiento reducido y notificaciones con la referencia inicial. No usar Share/Import como atajo para restaurar diferencias locales.

## Evidencia y recuperación

El estado DPAPI de `.181` incorpora las excepciones locales. Después de que esta versión guarde el outbox, `.174` rechazará ese archivo por el campo nuevo y detendrá sync conservando los datos. Para recuperar sync, volver a `.181` o `.179`; no borrar el archivo ni quitar campos a mano. Esta limitación de downgrade no cambia las preferencias locales ni la fecha de fin de sesiones existentes.

Registrar por cada checkpoint: versión instalada en ambas PCs, estado de cuenta/vínculo, consentimiento sync, valores compartidos antes/después, estado **Pending/Conflict/Enabled**, valores locales que se conservaron y si el reset produjo la revisión esperada. No incluir contraseñas, códigos MFA, recovery kit, access tokens ni claves.

Si aparece «Sync needs attention. Local settings remain available and unchanged.» / «La sincronización requiere atención. Los ajustes locales siguen disponibles y sin cambios.», detener el ensayo y conservar el texto exacto del error y el estado visible. No cerrar sesión, desvincular, borrar datos ni repetir una operación destructiva. La acción de reparación de retry solo debe aparecer para un checkpoint DPAPI dañado y tiene su propio diálogo de confirmación.

Una aceptación física debe registrar los resultados de los dos equipos y los conflictos observados. Una captura de «Linked» por sí sola no demuestra que el documento se descifró ni que los valores convergieron.

### Hoja de registro

Completar una fila por equipo y checkpoint. Se pueden anotar los valores observados en inglés o español; no copiar credenciales ni identificadores de sesión.

| Checkpoint | PC propietario | PC hermano | Observación |
| --- | --- | --- | --- |
| Setup `.181` y SHA-256 correctos | Pendiente | Pendiente | |
| Cuenta iniciada y dispositivo vinculado | Pendiente | Pendiente | |
| Consentimiento sync activo después de aceptar | Pendiente | Pendiente | |
| Cambio de tema convergió automáticamente en ventana abierta | Reportado PASS | Reportado PASS | 30–45 s según propietario |
| Cambio de idioma convergió automáticamente en ventana abierta | Reportado PASS | Reportado PASS | 30–45 s según propietario |
| Duración predeterminada convergió automáticamente en ventana abierta | Reportado PASS | Reportado PASS | 30–45 s según propietario |
| Cerrar/reabrir conservó sesión, vínculo y consentimiento | Reportado PASS | Reportado PASS | Propietario confirma ambas PCs |
| Conflicto offline de duración revisado y resuelto | Reportado PASS | Reportado PASS | 60 vs 40 min; propietario eligió Esta PC y confirma sincronización |
| Reset compartido llegó y aplicó defaults | Reportado PASS | Reportado PASS | Propietario confirma propagación |
| Conflicto de reset revisado y resuelto | Pendiente | Pendiente | Opción elegida: |
| Excepción local persiste, no publica y se retira al último global | Pendiente | Pendiente | |
| Exportación/importación portátil y selección parcial | Pendiente | Pendiente | |
| Importación respeta excepciones locales | Pendiente | Pendiente | |
| Reglas, perfiles, permisos, historial y prefs locales sin cambios | Pendiente | Pendiente | |
