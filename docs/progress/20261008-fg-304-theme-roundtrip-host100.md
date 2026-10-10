# FG-304 — Round-trip de tema claro/oscuro del Host instalado

Fecha: 2026-10-08
Estado: aceptado en Host .100; forced-colors y DPI permanecen pendientes.

## Prueba
En FocusGuard.UI .100, el botón accesible de apariencia anunció “Apariencia: Claro” al inicio. Lo invoqué una vez y pasó a “Apariencia: Oscuro”; la captura muestra el tema claro. Lo invoqué otra vez y volvió al estado inicial oscuro. Cerré el Host piloto, abrí el Host normal y el nombre accesible seguía indicando la misma acción “Apariencia: Claro”, confirmando que la preferencia original persistió tras el cierre/reapertura.

Las capturas aisladas del HWND FocusGuard muestran antes, tema alternativo y restauración:
- artifacts/fg298-output/fg302-theme-before.png
- artifacts/fg298-output/fg302-theme-other.png
- artifacts/fg298-output/fg302-theme-restored.png
El recibo UIA/persistencia está en artifacts/fg298-output/fg302-theme-roundtrip.json.

El servicio permaneció Ready/Inactive, sin SessionId. No se inició sesión ni se editó perfil/horario; sólo se cambió la apariencia temporalmente y se restauró.

## Alcance
Prueba modo claro/oscuro de la aplicación .100 y persistencia en reapertura normal. No prueba forced-colors, Windows 10/11, DPI ni la CSS de .101, que no está instalada.
