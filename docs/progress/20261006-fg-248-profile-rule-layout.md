# FG-248 — Reducir la densidad horizontal del editor de perfiles

## Diseño
La captura del editor clásico a 1409 px coloca navegación global, perfiles, sitios y aplicaciones en columnas simultáneas. El dashboard React ya separa el riel global y la lista de perfiles, pero su regla de CSS ponía Aplicaciones y Sitios una junto a otra mientras la ventana superara 1300 px. Eso vuelve a dividir la lectura en la resolución que mostró el usuario.

Ahora esas dos secciones permanecen en tarjetas separadas pero se apilan en una sola columna hasta 1450 px. La navegación global y la lista de perfiles mantienen sus lugares, y cada grupo conserva sus títulos, reglas, formularios y acciones. En ventanas más anchas se conserva la vista paralela. No cambié la semántica ni el orden de los controles y no agregué movimiento.

| Antes | Después | Por qué |
| --- | --- | --- |
| A 1409 px, las tarjetas de Aplicaciones y Sitios ocupan columnas paralelas junto al riel global y la lista de perfiles | Hasta 1450 px, las dos tarjetas se apilan; los rieles de navegación y perfil permanecen | Reduce columnas simultáneas y mantiene cada tipo de regla identificable |

## Verificación
- React:103/103.
- TypeScript, producción, contraste y CSP pasan. El verificador ahora valida el apilamiento hasta1450 px; Vite convierte la sintaxis de CSS a media query width<=1450px.
- Extensión:19/19; prueba estática del instalador pasa.
- Host empaquetado .84: los tres smokes locales salen0.
- El ZIP .84 abre, incluye manifest .84 y1,732 entradas; los18 archivos de UI coinciden por SHA-256 con el dist recién compilado. Los ZIP de Chrome y Edge conservan entradas requeridas y excluyen archivos de desarrollo/secretos.
- Graphify:10,218 nodos,27,718 aristas,1,005 comunidades. Setup/ZIP y hashes en D:\TEMP\focusguard-fg166-validation\artifacts\fg248\build-receipt.json.

## Límites y siguiente paso
No instalé .84. El self-test de Setup y su smoke gráfico no se ejecutaron; el token de Codex no está elevado. La UI activa sigue siendo .79, proceso32316; no la cerré ni modifiqué. No hay captura instalada de la disposición .84, así que el test de CSS prueba la regla empaquetada pero no sustituye la revisión visual de FG-242. Falta probar la ventana con perfiles reales y el ancho de referencia del usuario.