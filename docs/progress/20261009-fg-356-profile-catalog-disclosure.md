# FG-356 — Catálogo de aplicaciones bajo demanda

## Objetivo y diseño

La vista de Perfiles mostraba siempre el catálogo de aplicaciones detectadas debajo de las reglas guardadas. La lista extensa competía con la edición cotidiana y disparaba una consulta aunque el usuario no fuera a buscar una aplicación.

Lectura de diseño: herramienta de escritorio Windows para configurar sesiones y reglas de enfoque; se preservan graphite/mint, Geist, navegación y orden de los campos. Diales existentes: variación 3, movimiento 2 y densidad 6. Se usaron UI/UX Pro Max para criterios de teclado, etiquetas, foco y carga diferida; Emil y Apple para targets/foco y evitar movimiento decorativo; Frontend Design para una decisión visual propia del tema de FocusGuard; Taste solo para auditoría y preservación de marca, ya que su skill excluye dashboards y herramientas de producto de sus patrones de landing.

| Antes | Después | Motivo |
| --- | --- | --- |
| Catálogo, búsqueda y acciones visibles en cada visita; consulta al montar el componente; etiqueta de búsqueda solo para lector de pantalla. | Disclosure nativa cerrada inicialmente; consulta debounced tras abrirla; etiqueta visible y explicación enlazada al campo. | Las reglas guardadas y la edición manual conservan prioridad; el catálogo opcional no llena la pantalla ni hace trabajo hasta que se solicita. |

## Cambios y límites

- `ApplicationsPicker` usa `details`/`summary` controlados y conserva la lista mientras se cierra/reabre; no repite una búsqueda de la misma consulta al reabrir.
- El buscador tiene etiqueta visible. El resumen describe en inglés y español qué se puede hacer; la explicación de accesos directos del menú Inicio está asociada a la búsqueda.
- El resumen tiene un target de 68 px; la búsqueda, 44 px; ambos conservan foco visible y borde en forced-colors. No se agregó animación.
- No cambian el orden de las reglas/campos, la navegación, el método IPC, las acciones Allow/Block, la regla de ruta exacta, los datos ni los permisos.

## Verificación

- React: 136/136 pruebas; la prueba de `ApplicationsPicker` verifica que no haya consulta antes de abrir, que el disclosure y el copy EN/ES sean visibles, que abra la búsqueda y que reabrir sin cambiar el término no duplique la solicitud.
- Build React/TypeScript y `verify-dist.mjs` pasan. Contratos: contraste, navegación mínima, foco/forced-colors, reduced motion, CSS del disclosure y política CSP de recursos locales.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.125`: paquetes Chrome y Edge pasan.
- `scripts/test-installer.ps1`: pasa.
- Cinco smokes del Host empaquetado .125 (`--smoke`, `--smoke-runtime-missing`, `--smoke-renderer-failure`, `--smoke-renderer-crash`, `--smoke-window-dpi`): exit 0.
- Setup .125 SHA-256: `6E3F80F455D81CB9E9BDD527B213DB64974C612057FDBBDB303F37945E824B54`.
- Distribución ZIP .125 SHA-256: `2D052E89201BE13E90D49DD32790FE679A3F3EF9FC22F2479E0A06172B3E5198`.
- Graphify código: 11,637 nodos y 45,029 aristas.

## Estado de instalación y siguiente paso

.123 sigue instalado; .125 está empaquetado, pero no se instaló ni se verificó visualmente en el Host real. El Host instalado .123 permaneció abierto y sin cambios. Completar la aceptación de .125 en el Host propietario/LocalService cuando se ejecute por el flujo de instalación elevado autorizado. Después, continuar Fase 9 con Edge actualizado, prueba de FG-354 sin host nativo previo, reboot/recovery, DPI/monitor, accesibilidad de Windows y carga sostenida. Avalonia permanece disponible y Fase 10 no se inicia.
