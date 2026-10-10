# FG-347 — Reintentar la lectura inicial de Preferencias

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 8.6, Preferencias; aceptación instalada en Fase 9  
**Estado:** arreglo fuente probado y empaquetado en Setup .118; aceptación instalada pendiente.

## Síntoma y causa

En el Host .112, la primera entrada a Preferencias mostraba «No se pudieron guardar las preferencias» y ocultaba todos los controles. Pulsar «Actualizar» hacía que las preferencias cargaran.

Durante `host.initialize`, `DashboardClient` inicia `profiles.list`. React solicita `preferences.get` al montar; el cliente permite una sola solicitud pendiente y rechaza la segunda con `RequestInProgress`. El Host también serializa sus solicitudes. Otras lecturas vuelven a intentar ese error; `loadPreferences()` no lo hacía. El panel reutilizaba el mensaje de guardado para el fallo de lectura y no renderizaba los controles mientras `preferences` era nulo.

## Cambio

`loadPreferences()` reintenta `RequestInProgress` hasta tres veces, con 300 ms entre intentos, sólo mientras el mismo documento siga activo. La regresión simula tanto el `profiles.list` pendiente de arranque como una respuesta ocupada del Host, y confirma que la preferencia se termina leyendo. No se cambió IPC, autorización, políticas ni la aplicación del bloqueo.

## Validación

- `npm test`: 122/122 pruebas en 21 archivos.
- `npm run build`: TypeScript, Vite, CSP, contraste, layout mínimo, reduced motion y verificación de assets locales pasan.
- `scripts/build-installer.ps1 -Version 0.1.118` con salida aislada: Setup standalone y ZIP creados; manifest `win-x64` con 31 archivos.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.118`: Chrome y Edge pasan.
- `scripts/test-installer.ps1`: pruebas del instalador, paridad de archivos y token interactivo a integridad medium pasan.
- Cinco smokes del Host .118 (`--smoke`, runtime missing, renderer failure, renderer crash y window DPI): todos terminaron con código 0.
- `graphify update .`: 1551/1551 archivos AST; 11267 nodos, 40706 aristas y 1029 comunidades.

## Artefactos y límites

- Setup: `artifacts/installer/FocusGuard-0.1.118-Setup.exe`, 442853560 bytes, SHA-256 `FBBE32675A46C21D1FAEEE5693CD66B0BA871FCD03C2A7857D3C27F68CEF23A3`.
- ZIP de distribución: `artifacts/installer/FocusGuard-0.1.118-win-x64-menu.zip`, 699170893 bytes, SHA-256 `357FC8C1A71613D49CF1A87274B4681DBB827110D2951E6C98B80D256D1C7965`.
- Payload: `artifacts/setup-payload/0.1.118-fg346-preferences-retry/FocusGuard.Payload.zip`, 326385013 bytes, SHA-256 `DF51792C025F31EE2CB0B033AFEE47BD03EED27A593FFFCE490E7327F1D880C6`.

La instalación sigue en .112; .118 no se instaló ni se verificó visualmente. Por eso no se declara aceptada la lectura al abrir por primera vez en el Host instalado. La preferencia de sesión que dejó FG-346 se restauró desde la UI a `SimpleTimer` y se verificó como `lastSessionMode=0`; el servicio sigue `Running` bajo `LocalService`, Extension disponible y sin sesión activa.

Si la lectura falla por una causa distinta de `RequestInProgress` después de los reintentos, el texto de error de la pantalla todavía dice «No se pudieron guardar»; una copia de error de lectura separada queda como mejora de UX.

El empaquetado regeneró `src/FocusGuard.Setup/focusguard.ico` mediante el generador existente desde el PNG de marca; los cuatro PNG de `extension/icons/` conservaron exactamente sus bytes.
