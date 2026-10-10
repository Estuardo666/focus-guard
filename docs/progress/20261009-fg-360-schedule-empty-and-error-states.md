# FG-360 — Estados vacío y error de Programación

## Hallazgo visual

La pantalla de Programación del Host instalado .123, en 1216×799, muestra encabezado y acciones con el área de contenido vacía. El árbol de accesibilidad tampoco explica esa zona. En ScheduleEditor, el estado schedulesError del cliente del dashboard no se representa; el componente solo muestra errores de escritura en scheduleError. Por ello, una lectura fallida no ofrece motivo ni recuperación visible. Además, una lista cargada correctamente sin horarios solo recibe un párrafo genérico.

## Plan de implementación

1. Mostrar schedulesError como alerta EN/ES, con una instrucción para actualizar horarios.
2. Presentar el estado vacío únicamente tras una respuesta correcta, sin horarios y fuera de carga o error.
3. Dar al estado vacío jerarquía y explicación propias del producto, manteniendo la acción principal existente y respetando los tokens, teclado y forced-colors.
4. Probar la diferencia entre respuesta vacía y lectura fallida, ejecutar las verificaciones de React y empaquetar un candidato standalone .128.

## Impacto

- UX: evita que una lectura fallida parezca una lista vacía y explica qué hacer; la lista vacía real explica cómo se comportan los horarios.
- Seguridad y recuperación: no cambia IPC, permisos, datos, perfiles ni sesiones. La recuperación vuelve a usar schedules.list mediante el control existente.
- Instalación: .123 sigue instalado; no se instalará el candidato en este checkpoint. Validación real pendiente en el Host con el servicio LocalService.

## Cambio y verificación

- La vista de Programación ahora muestra el error de lectura localizado, indica cómo volver a intentar y oculta la lista retenida de una lectura anterior. Mientras exista el error, deshabilita la edición para no enviar revisiones obsoletas.
- Una respuesta correcta sin horarios muestra una tarjeta sencilla con explicación localizada. No usa ilustración ni animación; conserva graphite/mint, teclado, tokens y forced-colors.
- UX_SPEC exige explícitamente que una lectura fallida no parezca lista vacía.
- Archivos: ScheduleEditor.tsx y ScheduleEditor.test.tsx; tokens.css; locales en.json/es.json; package.json/package-lock.json y extension/manifest.json (.128); UX_SPEC.md; TASKS.md; FOCUSGUARD_UI_MIGRATION_PLAN.md; este checkpoint; captura previa en artifacts/fg360-output; graphify-out actualizado.
- Pruebas nuevas: estado vacío correcto en EN/ES; fallo de lectura en EN/ES que no muestra datos retenidos ni permite editar.
- npm test: 142/142 en 23 archivos, incluidos ambos estados EN/ES.
- npm run build: TypeScript, Vite, CSP, contraste, layout y verify-dist pasan.
- scripts/build-installer.ps1 -Version 0.1.128 -OutputDirectory D:\TEMP\FocusGuard-0.1.128-fg360-schedule-state -PayloadSuffix fg360-schedule-state -SetupPublishSuffix fg360-schedule-state: pasa tras alinear las versiones de package.json, package-lock.json y extension/manifest.json a .128.
- build-installer.ps1 .128: empaquetó el Host y el instalador standalone en D. El primer intento detectó que el manifiesto de extensión seguía en .127; se alinearon package.json, package-lock.json y extension/manifest.json a .128 y se reconstruyó con éxito.
- scripts/test-installer.ps1: pasa.
- scripts/test-browser-extension-package.ps1 -Version 0.1.128: Chrome y Edge pasan.
- Cinco smokes del Host .128: --smoke, runtime-missing, renderer-failure, renderer-crash y window-dpi; todos exit 0.
- El ZIP tiene 1,732 entradas y testzip devuelve None.
- graphify update .: 11,663 nodos y 45,297 aristas.
- Setup SHA-256: 982F6E391D7BDA63CE9B69A8E7104D43DC7DA2ADF620961934580F30BE1E0582.
- ZIP SHA-256: B2561111D0B4BBF03F821AAF368AA639B75BEA84E779861AFFF4A4F9D9437BA9.

## Estado instalado y siguiente paso

El servicio LocalService .123 y el Host .123 siguen instalados y en ejecución. Abrí el Host .128 directamente desde el payload como vista previa: el servicio .123 lo rechazó por no estar instalado y la pantalla mostró «Servicio no disponible» junto con la alerta localizada de Programación. Esto verifica el estado de error visualmente, no la aceptación del Host instalado; no se tocaron perfiles, horarios ni sesiones. La captura está en artifacts/fg360-output/fg360-host-0.1.128-read-error-with-0.1.123-service.png.

El payload .128 está en D:\TEMP\FocusGuard-0.1.128-fg360-schedule-state; el Setup standalone está en D:\TEMP\FocusGuard-0.1.128-Setup.exe y el ZIP en D:\TEMP\FocusGuard-0.1.128-win-x64-menu.zip. No se instaló ni se alteró la configuración de FocusGuard. La aceptación visual del estado vacío exitoso y de los horarios sigue pendiente en el Host instalado con servicio LocalService.

Siguiente gate: actualizar a .128 y aceptar en el Host real la alerta de lectura fallida, el estado vacío tras lectura correcta y un horario de 1 minuto con su fin programado. Después siguen los gates de Fase 4/5/9 y la Fase 10 permanece cerrada.
