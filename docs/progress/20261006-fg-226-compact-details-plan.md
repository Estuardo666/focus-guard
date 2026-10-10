# FG-226 — Plegado de detalles del modo compacto

## Diseño

Classic conserva tiempo, perfil, fase y avisos al plegar fin, objetivo, salud y controles de TopMost. Llevar esa jerarquía al Host React sin tocar datos de sesión ni propiedad de bandeja. Mantener la acción `Abrir vista completa` visible y un control accesible `Más detalles` / `Ocultar detalles`.

El Host usa 480×340 DIP expandido y 480×220 DIP plegado; el servicio continúa siendo la fuente de tiempo y fase. El bridge nativo informa el cambio al documento vigente y redimensiona la ventana sin animación. La versión 0.1.73 ya se generó y compartió; esta paridad se entrega como 0.1.74.

## Criterios

- Plegar oculta fin exacto, objetivo, salud del servicio, TopMost y nota de cierre.
- Siempre quedan visibles tiempo, fase, perfil, avisos, refrescar cuando corresponde y la acción de abrir vista completa.
- Expandir/restaurar mantiene bounds de ventana y devuelve fin, objetivo, salud y TopMost.
- El cambio sólo modifica el tamaño/presentación del Host. El bridge valida origen, token y booleano exacto, sin llamadas de sesión.
- Pruebas React/Host, Release build, CSP, Graphify y Setup/ZIP 0.1.74 validan el payload.
