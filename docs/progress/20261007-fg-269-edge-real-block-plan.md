# FG-269 — Plan para probar Edge con el paquete .91

## Hallazgo actual

El registro HKCU de Native Messaging Edge apunta a `C:\Program Files\FocusGuard\native-messaging\edge.json`, y el ZIP Edge instalado coincide con la distribución .91. No hay copia de la extensión FocusGuard bajo ningún perfil Edge; el proceso NativeMessaging actual proviene de `chrome.exe`. Por eso la conexión global del Service no prueba Edge.

## Prueba acotada

1. Edge existente está en `edge://extensions`; Developer Mode ya estaba `On` antes del ensayo. Cargar la carpeta unpacked .91 con el `key` público que conserva el extension id esperado.
2. Confirmar la tarjeta FocusGuard .91 con el ID fijo y un `FocusGuard.NativeMessaging` cuyo ancestro sea `msedge.exe`.
3. Crear un perfil temporal con `Block: FocusGuard.TestTarget.exe` y `Block: 127.0.0.1`. Revisar y activar un timer normal1min con bloqueo activado y Strict apagado; la review debe enumerar ambas reglas y el fin exacto.
4. Iniciar TestTarget cooperativo desde bin/Release y navegar una pestaña de Edge a `http://127.0.0.1:49321/edge-test`, usando un puerto loopback sin listener. Verificar que la página de FocusGuard redirige antes del error de conexión y que TestTarget termina; no visitar ni bloquear sitios personales.
5. No enviar Stop; esperar `Expired`, comprobar reconexión/status, retirar perfil de prueba y extensión unpacked. Mantener Developer Mode `On`, su estado previo; no cambiar políticas de Edge.

## Gates y límites

No modificar reglas de perfiles existentes. Si Edge policy bloquea la carga unpacked o los pipes nativos, detener el ensayo y documentar la causa; no cambiar políticas de organización ni lanzar perfiles aislados nuevos para eludir el bloqueo. La prueba de Edge no equivale a publicación o firma en Edge Add-ons. Precondición actual del Service: `Ready`, sesión `Expired`, Strict=false. La extensión .91 ya está cargada en Edge y NMessage bajo msedge; el intento de UIA anterior de navegar a `focusguard.invalid` se convirtió en búsqueda Bing y no cuenta como prueba. La revisión automática bloqueó crear un servidor HTTP loopback; no se inició ningún listener. El ensayo usará un puerto cerrado y distinguirá redirección de error de conexión; Edge está actualmente en `edge://extensions`.
