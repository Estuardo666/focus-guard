# FG-224 — Pie de acciones visible en Programación

## Plan

El formulario de horario es largo y el modal tenía `overflow:auto` en todo su contenido: «Revisar horario»/«Confirmar horario» quedaba al final del scroll. El contrato UX pide que la acción primaria siga visible. Se separó cuerpo y pie sin cambiar validación, permisos, CAS ni persistencia.

## Implementación

- `ScheduleEditor.tsx`: cuerpo `schedule-dialog-body` con `role=region`, nombre accesible y `tabIndex=0`; el form conserva todos los campos y el review handler. El pie de acciones es hermano del cuerpo; «Revisar horario» usa `form="schedule-entry-form"`, y Back/Confirm/Cancel mantienen sus handlers existentes.
- `tokens.css`: el dialog limita altura, el cuerpo hace scroll y el pie conserva superficie, borde y controles de 44 px en la parte inferior; en pantallas estrechas los botones comparten espacio.
- `ScheduleEditor.test.tsx`: comprueba pie fuera del cuerpo desplazable, asociación del submit al form, región accesible y que Cancel no envía `schedule.save`.

## Verificación

- `npm test`: 18 archivos, 84/84 pruebas aprobadas.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: build correcto, 0 advertencias y 0 errores; incluye TypeScript, Vite y verificación de CSP/recursos locales.
- `graphify update .`: 10.053 nodos, 25.680 aristas, 990 comunidades. Graphify omitió `graph.html` porque excede su límite visual de 5.000 nodos.
- Setup standalone 0.1.72: 30/30 recursos del manifiesto, ZIP con 1.731 entradas; Setup SHA-256 `99D40D7E71E19F6504A80FEBEFB84690558A3A4A2AE5457195195DFC1820C1FD`; menú ZIP `0DB0C5B6D945303B5747946A5131100D13D2A872BBA0ABADD2E86EB4370CA861`.
- El chunk `ScheduleEditor.js` de E y el payload 0.1.72 coinciden por SHA-256 `E9A17C23946A16AC129C648918FD8B00D36E34B103F3A2DE5FCA775785FEA8B4`.
- El paquete no está instalado; por ello la verificación visual del nuevo pie en WebView2 sigue pendiente. El Host instalado continúa en 0.1.71.
## Revisión de artefactos (2026-10-06)

- E `dist` y el chunk incluido en el paquete standalone 0.1.72 coinciden: SHA-256 `E9A17C23946A16AC129C648918FD8B00D36E34B103F3A2DE5FCA775785FEA8B4`.
- El chunk instalado y el paquete 0.1.71 coinciden por SHA-256 `F6F034C54057897A3F0FB5563083C1EAF69D1E7FE8DA805AC2D42BA57673E857`; 0.1.72 no está instalado.
- El manifiesto 0.1.72 contiene 30/30 archivos y el ZIP 1.731 entradas; Setup independiente y Setup dentro del paquete tienen SHA-256 `99D40D7E71E19F6504A80FEBEFB84690558A3A4A2AE5457195195DFC1820C1FD`.
- No se realizó instalación ni captura visual. El token de esta ejecución no está elevado y la herramienta `orca` para inspección de escritorio no está disponible.
## UX y límites

La confirmación queda disponible sin buscarla al final del contenido. Navegación por teclado mantiene un region focusable, y la acción de cancelar permanece fuera del form. No se añadieron animaciones ni se cambió copy, policy o datos. El test no reemplaza screenshot instalado a 1216×799/1024×700.

## Siguiente paso

Realizar el smoke visual instalado con0.1.72, después de elevar el instalador con permiso explícito. La aceptación real de browser F223 continúa pendiente por la política que rechazó crear un Chrome temporal; el paquete no se probó en Chrome.
