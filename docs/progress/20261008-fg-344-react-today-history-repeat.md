# FG-344 — Hoy, cronología y borrador Repeat en React

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 8.4, actividad e historial  
**Estado:** recorrido de hoy, resumen y borrador Repeat verificados en React .112; sin nueva sesión.

## Verificación

Con el servicio instalado en `Stopped`, la UI `Actividad` mostró el encabezado Hoy, la sección de cronología y diez filas accesibles con diez botones «Ver resumen». Entre las filas aparecen las sesiones de prueba de `gpt test` y del perfil `2`.

Abrí el resumen de la sesión de prueba `gpt test` de 8:17–8:19 p. m. El botón «Revisar y repetir esta configuración» abrió el borrador React con `gpt test`, dos minutos, Strict apagado y bloqueo activado; el botón de revisión quedó habilitado. No se revisó ni inició una nueva sesión. Al terminar, devolví el borrador a perfil `2`, dos minutos, bloqueo activado, Strict apagado, y la navegación a Resumen.

## Estado final y límites

Service y Extension siguen listos; SessionState=`Stopped`, `EnforcementConnected=false`, Strict apagado. El CRUD temporal FG-343 ya había devuelto el almacén a cinco perfiles. Esta pasada valida el resumen Today, las filas y el borrador Repeat; no valida capturas, detalle de objetivos/reflexiones, ni repetir y completar una sesión nueva. No hubo modificaciones a reglas ni al historial.

## Evidencia

- `artifacts/fg344-output/fg344-react-today-history.json`: cabecera Today, diez filas/resúmenes accesibles y perfiles de las sesiones de prueba.
- `artifacts/fg344-output/fg344-react-session-summary.json`: fila histórica seleccionada y acción Repeat visible.
- `artifacts/fg344-output/fg344-react-repeat-draft.json`: borrador restaurado por Repeat y formulario devuelto a los valores previos.

La instalación sigue en .112; los cambios de perfil sticky de .117 aún esperan instalación y captura visual instalada.
