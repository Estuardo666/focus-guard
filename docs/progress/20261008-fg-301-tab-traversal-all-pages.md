# FG-301 — Tab en las siete páginas React instaladas

Fecha: 2026-10-08
Estado: recorrido de teclado por páginas aceptado en Host .100; accesibilidad de sistema y .101 pendiente.

## Prueba
Con FocusGuard.UI .100 instalado, abrí el Host piloto como usuario normal y usé UI Automation de Windows. Desde el enlace “Ir al contenido”, Tab llegó a Resumen. En cada página recorrí con Tab todos los focos expuestos por su árbol WebView2 hasta volver a la pestaña seleccionada; las flechas verticales seleccionaron la siguiente página. No usé Enter ni Space.

| Página | Focos del árbol UIA |
| --- | ---: |
| Resumen | 13 |
| Perfiles | 19 |
| Actividad | 8 |
| Programación | 8 |
| Preferencias | 7 |
| Conexión | 6 |
| Ayuda | 6 |

El botón “Revisar sesión” se expuso con nombre accesible y habilitado en modo piloto. No se activó. Los nombres dinámicos de perfil/campo se omitieron del recibo para preservar datos. El servicio conservó ServiceReady/ExtensionReady, estado Inactive y SessionId nulo. La ventana se cerró normalmente.

## Evidencia
artifacts/fg298-output/fg300-all-pages-keyboard-host100.json

## Límite
Esta prueba cubre el árbol y orden de Tab de las siete páginas .100 en Pilot. No cubre Narrator, forced-colors, DPI, contraste visual de cada foco, rutas de diálogo/modal, Edge ni el CSS .101 aún no instalado.
