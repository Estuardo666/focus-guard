# FG-298 — Jerarquía de la vista solo lectura

Fecha: 2026-10-07
Estado: implementado en fuente y Setup .101; aceptación visual instalada pendiente.

## Problema y decisión
La captura instalada .100 mostraba “Solo lectura” y un botón “Revisar sesión” deshabilitado, pero la opción Temporizador conservaba el borde mint de activa y los campos bloqueados parecían editables. El aviso junto al botón era pequeño y estaba alineado a la derecha. Source confirmaba que allowed y disabled ya impedían preparar/iniciar; el defecto era visual y de copy.

Mantengo los valores de perfil/duración visibles como vista previa, sin habilitar el formulario. El subtítulo distingue “vista previa” de la acción disponible, el texto asociado al botón indica cómo iniciar desde un contexto autorizado, y los controles disabled usan fondos y bordes neutrales. La opción de sesión seleccionada pierde el acento activo en un fieldset disabled. La interfaz sigue sin animaciones decorativas.

## Skills de diseño aplicadas
- UI/UX Pro Max: jerarquía, legibilidad del estado disabled, foco y requisitos accesibles.
- Emil: respuesta y estados directos, sin animar navegación ni controles de uso frecuente.
- Taste y Frontend Design: identidad propia de FocusGuard, Geist, grafito/mint semántico y copy específica; no aplicar patrones de marketing a un dashboard.
- Apple Design: propósito, agencia y estado claro; conservar valores y no insinuar que se puede actuar.

## Archivos de producto
- src/FocusGuard.UI/src/DashboardStart.tsx: copy contextual y nota descriptiva en la acción.
- src/FocusGuard.UI/src/DashboardStart.test.tsx: regresión para subtítulo, relación aria-describedby y controles disabled.
- src/FocusGuard.UI/src/locales/es.json, en.json: copy localizada.
- src/FocusGuard.UI/src/design-system/tokens.css: preview neutral, callout legible y bordes de alto contraste.
- src/FocusGuard.UI/package.json, package-lock.json, extension/manifest.json: versión coordinada 0.1.101.
- docs/design/FOCUSGUARD_DESIGN.md: contrato para estados de sesión en solo lectura.

## Verificación
- npm test en React: 114/114.
- npm run build en React: TypeScript, Vite, contraste dark/light, CSP y assets locales pasan.
- npm test en extension: 19/19.
- scripts/test-browser-extension-package.ps1 -Version 0.1.101: pasan IDs, manifest, hashes y contenido Chrome/Edge.
- scripts/test-installer.ps1: pasan parseo y contratos de instalador/paridad.
- graphify update .: 10.334 nodos, 28.335 aristas; omitió solo la visualización HTML por superar el límite configurado.
- scripts/build-installer.ps1 -Version 0.1.101 -OutputDirectory artifacts/fg298-output/FocusGuard-0.1.101: completó el Setup independiente.
- artifacts/fg298-output/build-receipt-0.1.101.json: versión .101; 31/31 archivos de manifest; 18/18 recursos UI con SHA-256 idéntico a dist; ZIP Chrome/Edge iguales a los que se validaron.
- Setup: 442.849.464 bytes, SHA-256 F43FAF774771B5750428D56829B4228D2CF8D1BBE97D021C56290BC8A4853D05.
- ZIP: 699.168.514 bytes, SHA-256 467260DF257F4908ACDD15E1AB43BD13C7A1668AE346A6FCD4ED5A5671B85C8E.

## Seguridad y límites
No cambian IPC, roles, consentimientos, bloqueo o autoridad del servicio. El DrillClient instalado siguió Ready e Inactive sin SessionId. La terminal no está elevada. Abrí Setup .101 con lanzamiento normal; la ventana FocusGuard Setup quedó en PID23292. UI Automation desde el proceso de nivel medio no expuso controles y PrintWindow fue denegado; no automaticé UAC ni el botón Install. El manifiesto instalado confirma que sigue .100; DrillClient mantiene ServiceReady e Inactive, sin SessionId. Setup permanece abierto y requiere que el owner complete la acción Install en esa ventana. No cambié ACLs. La captura posterior del nuevo estilo y la verificación de modo piloto, teclado, forced-colors y DPI siguen pendientes.

## Siguiente paso
Instalar el Setup .101 en un contexto owner-present con elevación normal y revisar la captura a 1216×799. Confirmar que la vista de solo lectura parece un preview, que su aviso se asocia al botón y que el modo piloto conserva el formulario activo. La versión .100 permanece como referencia instalada para rollback.

## Smokes adicionales del Host .101
Tras generar el Setup, ejecuté el Host publicado dentro del payload con --smoke, --smoke-runtime-missing, --smoke-renderer-failure y --smoke-renderer-crash. Los cuatro salieron con código 0. El servicio instalado conservó PID 5704 y permaneció Ready/Inactive; no había SessionId antes ni después. Evidencia: artifacts/fg298-output/fg298-host-smokes.json. Esto valida runtime y recuperación del Host publicado, no la ventana interactiva instalada .101.

