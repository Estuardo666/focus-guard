# FG-218 — Preset de ciclo predeterminado por perfil

## Resultado

El Host proyecta el preset predeterminado de `ProfileCycleSettings` (id, nombre y configuración) en el resumen de perfiles, el detalle del editor y el perfil del intent de repetición. El ciclo React preselecciona esa configuración y muestra sus duraciones aunque el preset no esté en la página actual; el usuario todavía puede elegir otro preset para esa sesión. Profile permite guardar o quitar el preset predeterminado con el mismo compare-and-swap que protege las excepciones de descanso. Los conflictos recargan desde Host sin repetir la escritura.

No se cambió la persistencia ni el default existente de ningún perfil; sólo se guarda al confirmar explícitamente el cambio. El CAS de `ProfileCycleSettings` mantiene el default junto con las excepciones Block-only.

La dirección visual mantiene el diseño de producto existente: Geist, graphite/mint, controles etiquetados, disclosure de ciclo con foco visible y sin movimiento adicional. UI/UX Pro Max verificó DTO/etiquetas; Emil y Apple respaldan el feedback inmediato sin animación de adorno; Front-end Design favorece el diseño local del producto. Taste excluye dashboards y se usó sólo como guía anti-decoración.

## Verificación

- React: 16 archivos, 81 pruebas aprobadas, incluido el preset predeterminado fuera de la página visible y su configuración mostrada al preparar un ciclo.
- Host: 140 pruebas aprobadas; pipe + SQLite verifican establecer/quitar default y preservar excepciones, con la revisión CAS vigente.
- Storage: 38 pruebas aprobadas; Client: 109/109; Desktop: tres pruebas enfocadas de ciclo/descanso aprobadas.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings, 0 errores; CSP, TypeScript y chunks locales aprobados.
- `graphify update .`: 10,005 nodos, 25,458 aristas, 985 comunidades.
- Package 0.1.67: 29/29 recursos del manifiesto, 0 ausentes y 1.730 entradas ZIP. SHA-256 del Setup `AA94451746795DC6C4649B4CA5C23EABE6CCD0815F8B5A790A04CD5888A22719`; menú ZIP `C826746F932D3216918E1F6002876E6800D4EA6F9D4EBAB95D4C54FE7A5AE78E`. No instalado.

## Pendiente

Las pruebas visuales y de continuidad con el Host instalado siguen pendientes. El intento de instalación 0.1.65 en esta sesión fue rechazado antes de modificar archivos porque el token de PowerShell no estaba elevado; 0.1.67 se mantiene sin instalar. El diagnóstico previo de sólo lectura confirmó servicio y extensión listos, estado `Inactive`.
