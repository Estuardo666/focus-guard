# FG-334 - Evitar que el enlace de teclado tape el título

**Fecha:** 2026-10-08
**Fases:** 6 y 9
**Estado:** corrección implementada y empaquetada en .114; inspección visual instalada pendiente.

## Auditoría y dirección de diseño

La captura de la UI instalada .112 a 1216 x 799 muestra «Ir al contenido» encima del título al recibir foco. El enlace estaba fuera del flujo en `top: 12px`; el título empezaba después de 44px de padding superior. En ventanas de hasta 720px, el padding de 28px empeoraba la superposición.

Se trató como una evolución localizada del producto, no como un rediseño completo. Se conservaron la navegación, Geist, los tokens Catppuccin, las superficies redondeadas y el Host nativo. Los diales fueron variación 3/10, movimiento 2/10 y densidad 6/10. La búsqueda combinada de UI/UX recomendó por error una página de registro de webinar; las búsquedas por dominio sí identificaron una herramienta de temporizador, así que se descartó la paleta y tipografía ajenas a la especificación FocusGuard. Taste se usó para el protocolo de preservación, no para importar patrones de landing page a una aplicación de escritorio.

No se agregó animación a la navegación de teclado: es una acción frecuente y el foco debe responder de inmediato. En colores forzados el enlace usa `Highlight` y `HighlightText`.

## Cambio

- Al enfocar el enlace, el contenido principal reserva 100px arriba en escritorio y 84px hasta 720px, dejando el título debajo del enlace visible.
- Se añadió una prueba React que conserva el enlace antes de la navegación y confirma que apunta a un `<main>` enfocable.
- `verify-dist.mjs` comprueba el contrato de espaciado y los colores de foco forzados en el CSS de producción.
- Se actualizaron los metadatos del paquete React y de la extensión a 0.1.114 para no sobrescribir el candidato de rollback .113.

No cambió la información de perfiles ni de sesiones, los permisos IPC, el servicio ni la lógica de bloqueo.

## Validación

- `npm test`: 120 pruebas pasaron en 21 archivos.
- `npm run build`: TypeScript, Vite y verificación del CSS de producción pasaron. Bordes editables: 3.46:1 oscuro y 3.67:1 claro; texto habilitado mínimo 4.94:1.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.114`: paquetes Chrome y Edge pasaron.
- `scripts/test-installer.ps1`: pasó.
- `scripts/build-installer.ps1 -Version 0.1.114 -OutputDirectory artifacts/fg334-output -PayloadSuffix fg334 -SetupPublishSuffix fg334`: terminó con código 0 y generó Setup autónomo y ZIP de distribución.
- Los cinco smokes del Host publicado .114 (`--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure`, `--smoke-renderer-crash`, `--smoke-window-dpi`) terminaron con código 0.
- El manifiesto del candidato declara 31 archivos. Los 18 assets React, incluidas las tres fuentes locales, están en el payload; sus 18 hashes coinciden con `src/FocusGuard.UI/dist`. El CSS empaquetado contiene la corrección.
- Setup SHA-256: `9F67C122975C25E6A2618E3D4FCFF65E00C52681A295769A32D11E1B50472C1F`.
- ZIP de distribución SHA-256: `FA1DD7E72746BDD5BF25364D61D08028A801177EEB22065892EABA6236D7C161`.
- `graphify update .` reextrajo 1430 archivos y actualizó el grafo. No generó `graph.html` porque 11053 nodos superan el límite de visualización de 5000.
- Tras crear el candidato, el servicio instalado siguió `Ready` como LocalService, la extensión `Ready`, la sesión `Inactive`, `Strict=false`, y el Host .112 permaneció en PID 23972 bajo `--pilot`.

## Límite y siguiente aceptación

No se instaló .114 ni se obtuvo una captura posterior del Host. La instalación .112 permanece disponible y sin cambios. Falta revisar visualmente el foco en la ventana instalada y completar las pruebas restantes de las fases 4 a 10; este checkpoint no declara paridad ni cierre de la migración.
