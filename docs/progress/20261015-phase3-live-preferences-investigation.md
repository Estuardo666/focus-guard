# Fase 3: preferencias recibidas sin refresco de la ventana principal

## Evidencia física del propietario

Ambas PCs usan `.175`, cuenta iniciada y dispositivo vinculado. El propietario resolvió un conflicto de duración eligiendo 17 frente a 50; el receptor aún mostraba 13. Tras cerrar/reabrir el receptor, duración, tema e idioma convergieron. En otro ensayo cambió el idioma: la vista de bandeja lo mostró mientras la ventana principal conservó el anterior; reabrir la app aplicó el idioma nuevo. Esto prueba recepción/persistencia, pero contradice la aceptación de convergencia visible en una ventana abierta. No cerrar Fase 3.

Mist/Ocean sigue local por diseño: `design-system/palettes.ts` y App guardan la paleta en almacenamiento de la vista; el contrato global contiene tema claro/oscuro, idioma y duración. No ampliar ese contrato durante este fix. La pérdida de sesión reportada antes de iniciar el ensayo es una incidencia separada de causa aún no demostrada.

## Investigación y plan

1. Skill investigate; inspección de App, dashboard client, preferencias del Host e historial local. Hipótesis específica: App lee preferencias al montar y aplica únicamente ese resultado o guardados explícitos, sin observar cambios de preferencias producidos por el worker externo. El cliente dashboard tampoco tiene refresco de preferencias de fondo.
2. Escribir una regresión que mantenga la ventana montada, cambie la respuesta externa de preferences.get y compruebe tema/idioma/duración nuevos sin preferences.set de eco. Demostrar el fallo antes de corregir.
3. Añadir refresco de lectura acotado al ciclo de vida del cliente, sin competir con mutaciones o resetear errores de guardado. App debe aplicar snapshots nuevos; no reconstruir la vista ni enviar cambios compartidos de vuelta.
4. Probar cambios remotos, escritura concurrente y cierre del cliente; ejecutar UI/Host relevantes, formato/check/build, regenerar `.176`, guard/self-test/test-installer y graphify.

## Límites

No instalar ni desplegar automáticamente. No modificar identidad, permisos, enforcement ni snapshots de sesión. La prueba física debe repetirse con el candidato corregido antes de afirmar actualización visible en vivo.

## Causa y corrección locales; seguimiento físico actualizado

App no aplicaba snapshots nuevos de preferencias, el cliente dashboard no observaba preferencias externas y DashboardStart trataba la duración como inicialización única. Ahora el cliente relee preferencias locales cada dos segundos cuando está inicializado, ocioso y sin escritura incierta; descarta respuestas de otro documento o anteriores a un guardado. Las lecturas de fondo ceden a acciones de primer plano y no ejecutan preferences.set. Stop elimina el temporizador.

App aplica los snapshots recibidos. El temporizador sigue los valores nuevos cuando no hay una edición/revisión local que conservar. PreferencesPanel actualiza campos limpios y mantiene los que el usuario editó sin guardar. No se agregó ningún campo al contrato cloud.

Regresión red/green: una ventana App montada recibe idioma inglés, tema nuevo y duración 50 por preferences.get. Retirando temporalmente solo el efecto nuevo de App, el idioma se mantuvo en `es` cuando se esperaba `en`; la prueba falló. Restaurados los bytes exactos del fix, pasa sin preferences.set de eco. Otras pruebas cubren prioridad del guardado/preparación de sesión, respuestas tardías, limpieza del temporizador y borrador sucio de preferencias.

Validación: suite UI completa 38 archivos/251 pruebas PASS; npm run check PASS. scripts/test-installer.ps1 PASS. Cambios: App.tsx, native/dashboard.ts, DashboardStart.tsx, PreferencesPanel.tsx y sus regresiones; versiones UI/extensión `.176`. No se repitieron suites backend porque identidad/API/cifrado no cambiaron.

Resultado local: build productivo `.176`, validación de archivos embebidos, guard de cliente, `--package-self-test` y Graphify pasan. Setup SHA-256 `ABD8ADF23EAAA01AB49711326C872255DECF164287B5522090A3C29CF09F42DB`; ZIP menú `C0CAA97F3A3E8AE6B90475CF23F87AEF1504F7846FFB1CBCFF8A2935BF00BABA`. No instalado ni desplegado por el agente.

Aclaración física del propietario: rectifica la observación previa; tema, idioma y duración se actualizaron solos, sin navegar por el sidebar ni reabrir. El clic coincidió con el refresco. El propietario estima 30–45 segundos, dentro del objetivo del runbook. Convergencia de los tres campos: PASS reportado. No se infiere que el sidebar sea causal. La aceptación física completa sigue pendiente para reinicio, reconexión, conflictos, overrides/portátil y preservación local.
