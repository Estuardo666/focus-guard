# FG-275 — Validación instalada de .94

El owner terminó la instalación del Setup .94. Los archivos principales instalados se compararon por SHA-256 contra D:/TEMP/focusguard-fg166-validation/artifacts/fg274/publish: 18/18 recursos de ui y 7/7 binarios clave coinciden. browser-extension/distribution.json declara la versión .94.

FocusGuard.Service PID18792 corre como LocalService. Una consulta de solo lectura de DrillClient devolvió serviceReady=true, extensionReady=true y sessionState=Inactive, sin SessionId. FocusGuard.UI .94 PID38352 seguía abierto en la sesión del usuario.

Para comprobar el arranque en frío del Agent en el Host nuevo, con sesión Inactive terminé Classic Desktop PID28416 y su Agent PID17768. El Host .94 permaneció abierto y lanzó Agent PID45296 como hijo suyo. DrillClient confirmó que el servicio siguió Ready/Inactive y sin SessionId; quedó un solo ProcessAgent. No inicié ni detuve una sesión ni lancé TestTarget.

La evidencia confirma la instalación, la salud IPC básica y el arranque en frío del Agent desde el Host .94. El bloqueo real de aplicación del checkpoint FG274 sigue siendo evidencia de .92. Aún falta una captura actual de .94 a 1216×799, validar teclado/foco y forced-colors, y observar el aviso Activating en el WebView2 instalado. El dato extensionReady no confirma que Edge haya cargado la extensión ni que el bloqueo web funcione. No se inició una sesión para forzar Activating.

Recibo actualizado y comparación de archivos: D:/TEMP/focusguard-fg166-validation/artifacts/fg274/build-receipt.json.