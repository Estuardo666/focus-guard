# Fase 3: plan de prueba de reanudación Host tras reinicio

## Hueco

La suite recreaba `HostSettingsSyncSession` directamente desde DPAPI, pero no probaba que un nuevo `HostAccountService` recuperara el consentimiento guardado mediante su refresco normal del vínculo del dispositivo y reiniciara el runner.

## Plan

1. Extender el fixture API local autenticado de la prueba Host de opt-in para devolver el estado de cuenta/dispositivo vinculado.
2. Después de publicar el baseline con consentimiento, disponer el primer servicio y crear otro `HostAccountService` y `IdentitySession`, manteniendo el vault del dispositivo, el consentimiento DPAPI, las preferencias y el documento.
3. Iniciar la comprobación normal de cuenta, esperar a que recargue el vínculo y membresía, y verificar que el servicio nuevo reanuda la sesión y lee el documento sin mostrar consentimiento ni publicar otro baseline.
4. Ejecutar la prueba focalizada, la suite Host y `graphify update .`; actualizar TASKS y este checkpoint con resultados.

## Límites

Solo se cambia cobertura local. No instalar ni desplegar el candidato. La aceptación física de sincronización entre dos PCs y cualquier paso que dependa de tener ambas PCs disponibles siguen pendientes.

## Resultado

- La teoría de creación ahora cierra el primer Host y crea otro, recorre `Get` → comprobación de cuenta/vínculo → membresía → reanudación. Comprueba consentimiento preservado, runner leyendo el documento y ausencia de otro POST de baseline o diálogo de consentimiento.
- El primer ensayo falló: la ruta de reanudación ignoraba la raíz aislada del fixture y consultaba el almacenamiento normal. Se pasa la raíz de pruebas únicamente bajo `LocalTest`; producción continúa usando la raíz del usuario.
- Pruebas focalizadas 3/3 y suite Host 244/244 PASS. Formato whitespace focalizado PASS. La comprobación global de formato falló por diferencias existentes fuera del alcance; no se modificaron esos archivos.
- `scripts/test-installer.ps1` PASS. `graphify update .` PASS: 24.219 nodos, 167.796 relaciones y 1.976 comunidades; visualización HTML omitida por tamaño.
- UX: cerrar el Host conserva consentimiento; no se vuelve a solicitar. Seguridad: sesión, firma de dispositivo y membresía se validan; el fixture de cuenta exige bearer. No se cambió la configuración instalada ni staging.
- Paquete `.174` regenerado y validado con guard de producción y `--package-self-test`, ambos PASS. Setup SHA-256: `D428FD37BE5FDE228E71A134A0ECF8A13460F3D5A47636E1B7FD44969E67658D`. ZIP `FocusGuard-0.1.174-win-x64.zip`: `50169947055A96D87677D067D9CDD70226D8ED271388485755AE807422C76225`. Estos hashes sustituyen los anteriores del candidato en el runbook vigente.
- Siguiente tarea: ejecutar la aceptación física en las dos PCs cuando estén disponibles. Sin instalación ni despliegue durante este checkpoint.
