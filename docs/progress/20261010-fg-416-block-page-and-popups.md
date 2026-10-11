# FG-416 — Página de bloqueo con la nueva marca y popups sin parpadeo

Fecha: 2026-10-10. Pedido del propietario tras validar .141 (bandeja y pantalla de bloqueo funcionando).

## Resumen

- **Página de bloqueo del navegador** (`extension/status.html`, `src/status-page.css`) rediseñada con el lenguaje zen de la app: atmósfera Aurora (tres campos difuminados que derivan despacio), orbe perla con respiración 0,98–1,02 cada 3,6 s y anillo de progreso fino con el degradado de la marca, marca «FocusGuard» en píldora de cristal, titular grande, botones en píldora, nota «Anotarlo para después» en tarjeta de cristal, tipografía Geist (empaquetada en la extensión con su licencia OFL). Claro/oscuro según el sistema; movimiento reducido y forced-colors respetados; foco con halo de 1 px como en la app. Misma lógica y mismos IDs (sin cambios en `status-page.js`, ni en la política o el bloqueo).
- **Popups sin parpadeo**: el popup se crea fuera de la pantalla; la página aplica idioma/tema/movimiento de las preferencias, pinta dos fotogramas y envía `surface.ready` (parser estricto, sin parámetros); el Host lo mueve entonces a su sitio. Si la página no avisa, se muestra a los 1,5 s tras cargar.
- **Esquinas redondeadas** en Windows 10 (que no tiene preferencia DWM): región redondeada de 16 px (escalada por DPI) para bandeja, avisos y resumen; Windows 11 sigue usando la preferencia DWM. La pantalla de bloqueo queda rectangular.

## Pruebas

- Host 187/187 (+2: `surface.ready` llega al shell; con parámetros se rechaza).
- React 208/208 (+1: el popup no pinta nada hasta aplicar las preferencias y después avisa `ready`; `shell.ready` ligado al token).
- Extensión 19/19; `test-browser-extension-package.ps1 -Version 0.1.142` OK (exige ahora `status.html`, CSS y fuentes en el paquete).
- Vista previa de la página de bloqueo con `chrome` simulado (sólo en scratchpad): oscuro 800×450, claro 1280×860 con nota abierta, 390 px sin scroll horizontal y Geist cargada; modo estricto muestra la hora de fin.
- `test-installer.ps1` OK. Instalador `artifacts/installer-r14/FocusGuard-0.1.142-Setup.exe` 364,718,264 bytes, SHA-256 `A5B857CBD2A7C8F5E4F18181B8AB93E58E2BCB39ED071E6BEACC5AEEF113AA2A`; `--package-self-test` 0.

## Seguridad / UX

Sin cambios de permisos de la extensión ni de CSP: las fuentes son locales del paquete. `surface.ready` sólo mueve el popup que lo envía. Los avisos siguen sin robar foco.

## Limitaciones

No instalado (requiere al propietario). Tras instalar, si el navegador usa la extensión desempaquetada de `C:\Program Files\FocusGuard\browser-extension\staging`, puede hacer falta «Recargar» en `chrome://extensions` / `edge://extensions` para ver la página nueva.
