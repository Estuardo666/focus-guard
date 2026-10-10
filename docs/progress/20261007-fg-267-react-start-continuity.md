# FG-267 — Inicio y continuidad del timer normal en React .91

## Resultado

Host .91 estaba conectado al Service .91 como usuario normal. DrillClient confirmó antes de iniciar: Service/extension Ready, estado `Inactive`, sin SessionId. UIA confirmó `Temporizador` seleccionado, duración cambiada de25 a1 minuto sólo en el formulario, Strict `Off` y «Activar bloqueo durante la sesión» `Off`.

La revisión mostró el perfil seleccionado, duración1, fin exacto del servicio y «Sólo temporizador. No se aplicarán restricciones». Tras el botón explícito `Confirmar e iniciar`, DrillClient recibió SessionId `5eda89b5-27ee-4cde-9993-6c75009baaa5`, `Active`, remaining44s, Strict=false y `enforcementConnected=false`.

Se cerró únicamente Host PID38160 mediante `CloseMainWindow`. Con Host cerrado, DrillClient siguió viendo la misma SessionId en `Active`, remaining19s. Reabierto `C:\Program Files\FocusGuard\FocusGuard.UI.exe --pilot` como PID38540, el Service devolvió la misma SessionId en `Active`, remaining16s. No se envió `Stop`; el Service la dejó expirar automáticamente a `Expired`, remaining0. La lectura final conservó el mismo SessionId, Strict=false y enforcement=false. El Host permaneció abierto y mostró el formulario para una sesión nueva tras la expiración.

JSON de estados: `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\installed-normal-session-continuity-0.1.91.json`. Captura post-expiración: `D:\TEMP\focusguard-fg166-validation\artifacts\fg266\installed-host-0.1.91-session-expired.png`.

## Cobertura y límites

Esto verifica que React .91 puede iniciar un timer normal revisado, leer tiempo/estado del Service y conservar SessionId al cerrar/reabrir sólo la UI, con expiración automática. No verifica enforcement, bloqueo de aplicación, tráfico/bloqueo Edge, ni el tiempo restante renderizado en una captura durante el estado `Active`; FG268 queda abierto para esa captura. No se cambiaron perfil ni reglas.
