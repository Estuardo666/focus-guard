# FG-221 — Layout responsive y aceptación instalada 0.1.71

## Plan

- Comparar el árbol E, la copia D:\TEMP, el paquete y los archivos instalados para elegir la fuente correcta sin descartar trabajo previo.
- Verificar las asociaciones etiqueta/control y el breakpoint del editor de excepciones de descanso.
- Revalidar tests/build, el host normal y el servicio instalado, incluida la prueba de rollback ya autorizada.
- Registrar límites de aceptación: modo `pilotEnabled=false`, handshake activo del Agent y DPI real.

## Resultado

El árbol vigente es `E:\FocusGuard\FocusGuard-Autopilot`: package.json/package-lock.json son 0.1.71 y `src/FocusGuard.UI/dist/chunks/ProfileEditor.js` coincide exactamente con el chunk del paquete/instalación (SHA-256 `3FAE4CEE102E7F9402ABC04F04AA88847E255A8022089A8610B44D6F443DB2E7`). `D:\TEMP\focusguard-fg166-validation` es una instantánea fuente anterior 0.1.59 (dist distinto), aunque contiene el paquete 0.1.71 y la evidencia de validación. No se revirtió ni se sustituyó ningún cambio previo del árbol E.

FG221 asocia label/control en los formularios de excepciones de descanso y pasa los break app/site editors a una columna bajo 1300 px. El paquete 0.1.71 se abrió como usuario normal. En modo default `pilotEnabled=false`, el caption “Vista de consulta” es correcto y las acciones de mutación están deshabilitadas; no se cambió ese copy ni se escribieron reglas, perfiles o programación.

## Verificación

- `npm test`: 17 archivos, 83/83 pruebas aprobadas.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: build completado, 0 advertencias y 0 errores; se ejecutaron TypeScript, Vite, verificación de CSP/assets locales y proyectos .NET.
- Package 0.1.71: 30/30 recursos, ZIP con 1731 entradas; Setup SHA-256 `F20456D708DE6BC17958F60D47B955F21E9181C5738C612944A8EE40AED8A888`; menú ZIP `30B8B5763290EF5278F5924D7A5000774B8F26D5935109DD29BE61241ED99E20`.
- Setup instalado: el servicio corre como `NT AUTHORITY\LocalService`; consulta autorizada devolvió Ready/extension Ready/Inactive. `untrusted-status` fue aislado; ambas instancias de servidor impostor fallaron con native 5; header/frame parcial se aisló en control, ProcessAgent y extension-health.
- Rollback de prueba falló intencionalmente después de reemplazar binarios; el instalador restauró payload y owner IPC. Post-check: serviceReady=true, extensionReady=true, sessionState=Inactive; hashes instalados coinciden con el paquete.
- Capturas instaladas: Help y Profile a 1024x700, Schedule y break settings a 1216x799. `Vista de consulta` corresponde al modo no piloto. La ventana volvió a sus bounds originales.

## Límites y siguiente paso

La sonda de IPC está compilada contra assemblies 0.1.30; autenticó el servicio instalado 0.1.71 y ejerció endpoints v2 compatibles, pero no demuestra un handshake Agent aceptado durante sesión. Tampoco se activó una sesión para probar bloqueos reales ni se midió DPI de monitor; los cambios de ventana sólo verificaron reflow CSS. El copy de Perfiles/Programación en modo piloto (`--pilot`) y sus mutaciones instaladas siguen pendientes.

Siguiente: preparar un target de prueba desechable y validar la UI `--pilot` sin modificar reglas existentes; después ejecutar un timer normal acotado para Agent handshake, bloqueo app/browser y cierre/reapertura de host; completar DPI/accessibility y distribución antes del cambio predeterminado.
