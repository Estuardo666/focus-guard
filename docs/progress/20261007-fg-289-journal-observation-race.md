# FG-289 — Observación de Classic conserva intención aceptada por React

Estado: código y paquete acumulado .100 verificados; aceptación instalada pendiente FG-291. La instalación actual comprobada sigue en .98.

Plan: reproducir la escritura de un observador con caché anterior a la aceptación en otro proceso. Impedir que ese fallback sustituya el consentimiento, objetivo y configuración aceptados. Mantener desconocido el consentimiento de entradas legacy.

Causa: RecordObservedSession comprobaba journal.Get en caché, luego Save releía el archivo pero reemplazaba incondicionalmente la entrada. Classic podía borrar el consentimiento explícito guardado por el Host. El diario instalado contiene enforcementEnabled=null para ambas sesiones nuevas de FG-288.

Cambio: SaveIfAbsent comprueba existencia bajo el mismo lock de archivo que la escritura; el fallback observado usa esa operación. Save de una aceptación explícita conserva su comportamiento. No se infiere ni repara consentimiento de sesiones antiguas.

Regresión StaleObserverCannotReplaceAnotherHostsAcceptedConsent falla con consentimiento null antes del cambio y pasa después para consent explícito true y false. Client111/111.

Host154/154, Desktop189/189, installer tests y React114/114 pasan. El paquete acumulado .100 incluye FG-290, manifest31/31, UI18/18, hashes de extensiones correctos y cuatro smokes Host exit0. Recibo D:/TEMP/focusguard-fg166-validation/artifacts/fg290/build-receipt-0.1.100.json. Pendiente instalarlo y crear sesión normal nueva para confirmar diario/replay con Classic y React conviviendo.

