# FG-263 — Reset de scroll al iniciar React Host

## Problema y cambio

La captura instalada de .89 mostraba el encabezado fuera del viewport en la sesión `Inactive`, porque WebView2 restauraba `scrollTop` antes del primer render. `src/FocusGuard.UI/src/App.tsx` reinicia `document.scrollingElement.scrollTop` de forma síncrona en el layout effect de montaje. `App.test.tsx` simula `scrollTop=360` al montar y comprueba que queda en cero; el caso previo `Active`→terminal también queda cubierto.

## Verificación de código y paquete

El recibo `D:\TEMP\focusguard-fg166-validation\artifacts\fg265\build-receipt.json` registra React106/106, TypeScript, build de producción, CSP/contraste, solución .NET874/874, extensión19/19, pruebas estáticas de instalador, Setup self-test y los tres Host smokes (`--smoke`, renderer failure y renderer crash) con salida0. Los tests del paquete browser pasan19/19; el manifiesto contiene31 archivos y los ZIPs Chrome/Edge contienen los hashes registrados.

Setup0.1.90: SHA-256 `2AA0E7ABAE1780CFE87963A0C0FBE629C89AB2D7CAECC91E5AC2FFC6A7E20255`. ZIP x64: `0F48703D1FC6CC9CB2DB2707585464119ADDDBC3F77442389DD8AFC11075977B`. ZIP de extensión Chrome/Edge: `C8932E02FAAB451396758BDB7C17DBBA08286D4274BF152E40DA3A2F44F47AB4`.

## Instalación owner-present y comprobación visual

El owner inició Setup .90 mediante el flujo normal UAC e indicó que pulsó `Instalar`. Después, `FocusGuard.DrillClient.exe status` devolvió `serviceReady=true`, `extensionReady=true`, `sessionState=Inactive`, `sessionId=null` antes y después de abrir el Host. SCM/WMI informa `FocusGuard` Running como `NT AUTHORITY\LocalService`, PID43268. `FocusGuard.UI.exe --pilot` se abrió como usuario normal, PID35608. Los18 archivos de `C:\Program Files\FocusGuard\ui` coinciden individualmente por SHA-256 con los18 archivos de `src/FocusGuard.UI/dist`.

La captura exclusiva de la ventana Host a1216×799 muestra el encabezado completo al primer inicio y el skip link `Ir al contenido` con foco visible: `D:\TEMP\focusguard-fg166-validation\artifacts\fg265\installed-host-0.1.90-screen.png`. Consultando el pane `FocusGuard - Web content` a través del renderer WebView2, UIA expone84 descendientes y20 controles interactivos; los20 reportan `IsKeyboardFocusable=true`. `Ir al contenido` es el elemento enfocado. Esto no sustituye una pasada completa de Tab ni una evaluación Narrator. El snapshot está en `D:\TEMP\focusguard-fg166-validation\artifacts\fg265\installed-host-uia.json`.

No se activó ni detuvo una sesión. La aceptación sólo cubre la primera vista instalada con la sesión inactiva; Edge real, bloqueo web, temas, forced-colors, DPI, teclado/Narrator, recuperación, carga sostenida y rollback permanecen pendientes en Fases7/9/10.
