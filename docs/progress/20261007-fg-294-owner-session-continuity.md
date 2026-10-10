# FG-294 — Continuidad de sesión instalada al cerrar el Host piloto

Fecha: 2026-10-07
Estado: aceptado.

## Prueba

La instalación es FocusGuard 0.1.100. Antes de comenzar, FocusGuard Service estaba Running como NT AUTHORITY\LocalService y el SessionId previo ya estaba Expired.

Se eligió el perfil existente gpt test y se aceptó una sesión normal de temporizador de un minuto. Enforcement estuvo activado; Strict permaneció apagado. El perfil no se editó y FocusGuard.TestTarget.exe no se inició.

El servicio reportó Active, enforcementConnected=true, SessionId 1a9e5900-bc6b-4233-9aad-10e87e0ddb21 y EndUtc 2026-10-07T23:45:26.9610478Z. El proceso Host React piloto PID 41300 se cerró ordenadamente. El estado consultado de nuevo mantuvo el mismo SessionId y EndUtc, Active y enforcementConnected=true. FocusGuard.UI normal PID 25072 permaneció abierto; su panel Resumen mostró gpt test, tiempo restante y Confirmada para los canales.

El temporizador terminó naturalmente. El servicio informó Expired, remainingSeconds=0 y enforcementConnected=false. El Service conservó PID 46424 y siguió Running como LocalService. No se envió Stop ni se reinició el servicio.

## Conclusión

Esta aceptación demuestra continuidad entre el Host React y el servicio instalado .100 cuando el Host se cierra. La UI normal seguía observando la sesión mientras estaba activa, y el timer finito liberó la política al vencer.

## Evidencia

D:\TEMP\focusguard-fg166-validation\artifacts\fg290\fg294-owner-session-continuity-0.1.100.json registra identidad/estado/PID del servicio, SessionId y EndUtc antes y después de cerrar el piloto, lectura del Host normal y resultado Expired. No hubo cambios de código fuente, por lo que no se recompiló ni se repitieron las suites automatizadas del paquete.
