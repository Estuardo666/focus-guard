# FG-339 — Aceptación instalada de sesión React y continuidad del Host

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fases:** 7, con evidencia adicional para 5 y 6  
**Estado:** validado en FocusGuard .112 instalado. El paquete .116 sigue sin instalar.

## Plan de verificación

1. Confirmar que el servicio instalado está en `LocalService` y responde a un cliente diagnóstico autenticado.
2. Seleccionar desde el Host React el perfil guardado `gpt test`, iniciar una sesión normal finita con bloqueo activado y modo estricto apagado.
3. Durante una sesión activa, ejecutar el binario benigno `FocusGuard.TestTarget.exe` configurado como regla Block y comprobar la terminación en el servicio.
4. Cerrar el Host con su cierre normal, volverlo a abrir como usuario estándar y comprobar que el servicio conserva la misma sesión y agente.
5. Guardar la captura React y las respuestas correlacionadas de status/actividad.

## Resultado

La UI React instalada .112 creó una sesión normal de un minuto con `gpt test`, bloqueo activado y strict apagado. El servicio confirmó `Active`, `ServiceReady=true`, `ExtensionReady=true` y `EnforcementConnected=true`. El proceso de prueba PID 29556 emitió tres heartbeats y terminó a los 2,86 s, antes de su duración natural de 20 s; el registro de actividad del servicio confirma `FocusGuard.TestTarget.exe` / `Terminated` bajo SessionId `54ade286-5ec9-4665-a274-85ffb673ddeb`.

Para validar continuidad se inició otra sesión normal de dos minutos desde la misma UI. El Host PID 19540 aceptó el cierre normal. Tras reabrir FocusGuard.UI.exe --pilot en la sesión del usuario, apareció la ventana visible PID 29404. Antes y después, el servicio conservó SessionId `1420d6bd-1208-4038-b9dc-f0c758bef393`, el mismo `StartUtc` y `EndUtc` (`2026-10-09T01:19:51.2676423Z`), estado `Active`, `EnforcementConnected=true`, `ServiceReady=true`, `ExtensionReady=true` y `StrictMode=false`. ProcessAgent mantuvo PID 24108. La captura posterior muestra el temporizador React en curso, modo normal, Servicio conectado, Extensión disponible y confirmación de bloqueo.

## Evidencia

- `artifacts/fg339-output/fg339-react-prestart-review.json` y `fg339-react-session-review.png`: revisión del primer inicio, perfil/reglas, fin exacto y aviso de riesgo antes de iniciar.
- `artifacts/fg339-output/fg339-react-session-start.json`: primera sesión React, estado de servicio y transición de EnforcementConnected.
- `artifacts/fg339-output/fg339-react-test-target.json` y `fg339-react-test-target-activity.json`: proceso lanzado, salida, SessionId y evento `Terminated` de la autoridad del servicio.
- `artifacts/fg339-output/fg339-react-session-continuity-run.json`: preflight de la sesión de dos minutos y status activo.
- `artifacts/fg339-output/fg339-react-host-continuity.json`: cierre normal/reapertura, igualdad de SessionId/EndUtc, Agent PID y estado de canales.
- `artifacts/fg339-output/fg339-react-active-after-reopen.png`: ventana instalada después de reapertura.

## Seguridad, UX y límites

Las dos sesiones fueron normales, de duración finita y con modo estricto apagado. No se modificaron reglas almacenadas; el bloqueo de aplicaciones afectó sólo al ejecutable de prueba. El proceso de FocusGuard.UI fue abierto sin elevación. El perfil contiene una regla Block para la aplicación y una regla Allow para `focusguard.invalid`, así que esta pasada no prueba bloqueo web. Tampoco prueba Edge, handshake Agent en un escenario de política activa independiente, strict/ciclos, Narrator, forced-colors ni un cambio real de DPI. La interfaz React está instalada en .112; los cambios de FG-337/338 incluidos en .115/.116 siguen pendientes de validación instalada.

La pantalla React activa deja visibles la cuenta regresiva, el fin exacto, modo normal, salud del servicio/extensión y confirmación de bloqueo; el texto aclara que cerrar la ventana no detiene la sesión. La prueba también confirma que cerrar y reabrir el Host no reinicia ni reemplaza la sesión autoritativa.

## Siguiente paso

FG-340 ya acepta el bloqueo web en Chrome .112. Instalar y aceptar .116 cuando una consola ya elevada esté disponible; repetir aquí el arranque/continuidad sobre ese binario, probar bloqueo en Edge y cerrar las comprobaciones restantes de Fase 4/7. La migración completa de las fases 4–10 sigue abierta.
