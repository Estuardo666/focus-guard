# FG-285 — Rollback owner-present de .98 a .97

## Prueba

Con FocusGuard instalado .97, inactivo y Service Ready, construí el payload candidato .98 y usé `scripts/install-focusguard.ps1` con el failpoint de prueba `-InjectFailureAfterCopyForRollbackTest`. El Setup .98 no se instaló como versión de producto; se usó el script de transacción con PackageDirectory .98 y consentimiento IPC ya provisto por el flujo normal.

El instalador suspendió Native Messaging, detuvo los procesos/service, respaldó el árbol .97, copió el paquete candidato y lanzó el fallo intencional tras reemplazar payload y cambiar registros. El catch restauró el árbol y configuración anteriores. El wrapper reporta el fallo esperado sin `Rollback also failed`, lo que implica que la comprobación interna del fingerprint terminó sin error.

## Verificación independiente

- Los 7 ejecutables y 18 recursos UI instalados coinciden con el publish .97.
- La distribución de navegador instalada vuelve a .97; el ZIP .98 no quedó en Program Files.
- Chrome y Edge vuelven a registrar `C:\Program Files\FocusGuard\native-messaging\*.json`; Run y el acceso directo Start Menu apuntan a los ejecutables .97 de Program Files.
- Service volvió a Running como LocalService PID24372. DrillClient devuelve Ready/extensionReady/Inactive, SessionId nulo. ProcessAgent PID22908 se reanudó; luego abrí otra vez el Host normal .97 PID5112.
- No cambié perfiles ni inicié o detuve sesiones.

La prueba está en `D:/TEMP/focusguard-fg166-validation/artifacts/fg280/owner-present-rollback-0.1.98.json`; el transcript detallado está en `owner-present-rollback-0.1.98.log`.

## Límite

El log System no devolvió eventos entre 15:39:30 y15:41:30; la consulta indicó que el registro más reciente era de15:35. No se provocó un crash de servicio para generar SCM7034, por lo que sigue pendiente correlacionar un evento real con los diagnósticos service-failures. El transcript del instalador imprimió un `ipc-control-rejection` con timestamp `2026-10-07T14:43:10Z` (`CallerIdentityUnavailable`, native error5); es anterior a esta prueba y no se debe atribuir al failpoint ni al rechazo de rol de FG-284.
