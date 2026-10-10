# FG-291 — Aceptación instalada .100 (parcial)

Fecha: 2026-10-07  
Estado histórico al 2026-10-07: aceptación parcial en .100. El addendum del 2026-10-08 cierra Repeat Classic en .102.

## Resultado

El propietario confirmó la instalación de Setup .100. En C:\Program Files\FocusGuard, los 18 recursos de ui y ocho ejecutables coinciden por SHA-256 con el paquete D:\TEMP\focusguard-fg166-validation\artifacts\fg290\FocusGuard-0.1.100\publish. El manifiesto de la extensión instalada declara 0.1.100. El servicio FocusGuard está Running bajo NT AUTHORITY\LocalService. Orca 1.4.222 está instalado, activo y con runtime listo.

Con FocusGuard Classic y el Host React piloto abiertos, se inició una sesión normal de dos minutos con el perfil existente gpt test. Strict permaneció apagado; el control de bloqueo estaba activado para una regla de aplicación Block y una regla de sitio Allow. El diario local guardó SessionId a4f7bbde-0308-4cc6-a27f-8bc4883ff0cf, profileId cbeb439e-426c-4a8a-80c6-9cbb388d7929 y enforcementEnabled: true. La pantalla mostró el estado Activating y el aviso de que el bloqueo aún no había comenzado; después confirmó los canales solicitados. La sesión terminó por temporizador y el historial React la registra como completada. No se inició una segunda sesión y no hubo intentos bloqueados en esta sesión porque no se ejecutó FocusGuard.TestTarget.exe.

En el historial React apareció Revisar y repetir esta configuración. Al abrirlo, la UI creó un borrador con perfil gpt test, duración de dos minutos y bloqueo activado, con el mensaje de que recuperó la configuración aceptada. La sesión no se volvió a iniciar.

En el Host normal, Programación mostró: “Esta ventana está en modo de solo lectura. Para editar, abre FocusGuard en modo piloto; FocusGuard clásico también está disponible.” El botón Nueva programación se ve deshabilitado; no se creó ningún horario.

## Pendiente

Al 2026-10-07 no se pudo comprobar Repeat Classic con Orca. La verificación posterior con UI Automation nativa se documenta en el addendum siguiente.

La prueba de bloqueo real de FocusGuard.TestTarget.exe permanece cubierta por FG-288 en .98; esta sesión de aceptación .100 no la repitió. El bloqueo web real y las restantes pruebas de las fases 4–10 siguen pendientes según la tabla actual del plan.

## Evidencia

Recibo JSON y capturas de Activating, Actividad, resumen Repeat, borrador Repeat y Programación están en D:\TEMP\focusguard-fg166-validation\artifacts\fg290\fg291-installed-acceptance-0.1.100.json y fg291-*.png.

La suite asociada al paquete ya está registrada en build-receipt-0.1.100.json: React 114/114, Client 111/111, Host 154/154, Desktop 189/189, scripts del instalador aprobados y cuatro smokes publicados con exit code 0. Esta aceptación no añadió pruebas automatizadas ni cambió código de producto.

## Cierre .102 — Repeat Classic (2026-10-08)

Con el Host React .102 y Classic en token medio, Service PID25096 LocalService y estado Inactive, se inició desde Classic una sesión SimpleTimer de 1 minuto en gpt test, enforcement=true, Strict=false. No se ejecutó FocusGuard.TestTarget.exe.

La preferencia existente SessionEndPresentation=Notification se conservó. UIA observó el toast Sesión completa dentro de los nueve segundos, abrió el resumen y encontró el botón Repetir sesión habilitado e invocable. El resumen mostró Completaste 1 minutos, gpt test y 0 bloqueos. Repetir inició directamente una segunda sesión con el mismo ProfileId, 1 minuto, enforcement=true y Strict=false; no creó un borrador. El diario registra SessionId 333a6921-7ed2-4b7b-b89e-c255f726fcd5. El Host refrescado a las 10:31 confirmó Servicio Conectado, Extensión Disponible y Sin sesión activa; el servicio nunca se detuvo. Repeat Classic quedó aceptado desde el resumen de cierre. En Classic la pestaña Actividad es de hoy; no se verificó una lista histórica persistente con acción Repeat.

La instalación también dejó un Desktop y ProcessAgent ocultos con token elevado. Se cerraron sólo esos procesos UI con la sesión inactiva y se relanzó Classic desde el shell normal; ambos quedaron en token medio, el Service permaneció LocalService. La causa del token elevado inicial queda abierta en FG-308.

Evidencia estructurada: artifacts/fg305-output/fg291-classic-repeat-0.1.102.json y artifacts/fg305-output/fg291-classic-restart-elevation.json.
