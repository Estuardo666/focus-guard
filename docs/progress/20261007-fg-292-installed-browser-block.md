# FG-292 — Bloqueo web real en Chrome con Setup .100

Fecha: 2026-10-07
Estado: Chrome aceptado. Falta repetir la validación con Microsoft Edge.

## Diseño de prueba

Se abrió Chrome con un user-data-dir temporal y sin sincronización. La extensión se cargó desde la distribución instalada C:\Program Files\FocusGuard\browser-extension\staging. Chrome informó FocusGuard 0.1.100 e ID emhdinkjpinjnmldipnejnbeefgonjnj, igual al origen autorizado en C:\Program Files\FocusGuard\native-messaging\chrome.json. El Native Messaging host se inició como hijo del Chrome aislado.

Se creó el perfil temporal Codex QA Edge Block 20261007 con una única regla: example.com en Block. Una sesión normal de un minuto usó enforcement activado y modo estricto apagado. El servicio confirmó Active, enforcementConnected=true, con SessionId fe128764-a78a-48fb-ad1f-09a785397c3b y profileId b635970e-85f2-4796-98c2-5a86db650edd.

## Resultado

La navegación a https://example.com redirigió al documento local de la extensión, chrome-extension://emhdinkjpinjnmldipnejnbeefgonjnj/status.html, con el mensaje “example.com will still be there later”. El resumen de Activity de React muestra la sesión completada, el perfil de prueba y Intentos bloqueados: 1.

El temporizador venció sin intervención. El servicio reporta Expired, remainingSeconds=0 y enforcementConnected=false. La pantalla de bloqueo se cerró automáticamente; una navegación nueva a https://example.com cargó el documento público Example Domain. Esto demuestra bloqueo y liberación real para Chrome con el componente instalado .100.

Se mantuvo Strict apagado. No se probaron ciclos ni se repitió la sesión. El perfil temporal fue eliminado después de expirar, el directorio de datos aislado de Chrome se borró y su ventana se cerró. El historial conserva la entrada de la sesión como evidencia; no se modificó el perfil habitual gpt test. El Chrome habitual permaneció abierto con su sesión sin cambios.

## Paridad Edge pendiente

Edge Guest muestra que Extensions no está disponible en modo invitado. Una primera ventana Edge con user-data-dir aislado inició automáticamente el flujo de cuenta Microsoft; se cerró antes de navegar y se eliminó el directorio temporal. No se tocó ni se navegó el perfil Edge habitual. Por tanto, esta aceptación prueba el bloqueo en Chrome; Edge sigue pendiente.

## Revisión de interfaz y diseño

La comprobación visual del bloqueo y del resumen aplicó las pautas de UI/UX Pro Max para jerarquía, nombres accesibles, mensajes de estado y contraste; la respuesta del bloque muestra dominio, tiempo restante y una acción principal. Las pautas de Emil y Apple se usaron para valorar respuesta inmediata y liberación finita del estado; no se añadió animación de adorno ni se cambió el diseño. Taste y Frontend Design orientan la continuidad del sistema existente: paleta oscura FocusGuard, acento violeta, tipografía Geist y copy directa. No se cambió código visual en esta aceptación.

## Evidencia y verificaciones

JSON y capturas:
- D:\TEMP\focusguard-fg166-validation\artifacts\fg290\fg292-installed-browser-block-0.1.100.json
- fg292-chrome-extension-loaded-0.1.100.png
- fg292-chrome-blocked-page-0.1.100.png
- fg292-chrome-released-after-expiry-0.1.100.png
- fg292-react-block-attempt-summary-0.1.100.png

El recibo de .100 mantiene las pruebas automatizadas y los smokes publicados ya documentados. FG-292 fue una aceptación manual del paquete instalado y no cambió código fuente.