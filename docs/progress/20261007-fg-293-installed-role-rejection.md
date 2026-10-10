# FG-293 — Rechazo de rol no autorizado en servicio instalado .100

Fecha: 2026-10-07
Estado: aceptado.

## Resultado

La prueba se ejecutó contra la instalación FocusGuard 0.1.100. El cliente instalado FocusGuard.DrillClient.exe status, registrado como cliente Diagnostic, recibió respuesta; el servicio indicó serviceReady=true y la sesión estaba Expired.

A continuación, FocusGuard.DrillClient.exe probe-agent-role-denied envió un heartbeat válido al pipe ProcessAgent usando el cliente de rol Diagnostic. El agente cerró la conexión sin respuesta; el cliente imprimió DENIED_EOF y terminó con código 0, que es el resultado esperado para el rol no autorizado.

El servicio permaneció Running bajo NT AUTHORITY\LocalService con PID 46424 antes y después. El SessionId fe128764-a78a-48fb-ad1f-09a785397c3b siguió Expired; el estado, el perfil y la sesión no cambiaron. No hubo mutaciones del servicio ni del perfil.

## Evidencia

D:\TEMP\focusguard-fg166-validation\artifacts\fg290\fg293-installed-role-rejection-0.1.100.json registra status permitido, DENIED_EOF, PIDs y estado de sesión antes/después. El paquete .100 instalado se verificó previamente en FG-291: los binarios/UI instalados coinciden por SHA-256 con el recibo acumulado.

## Alcance pendiente

Este rechazo confirma el aislamiento de los roles Diagnostic y ProcessAgent en la instalación real. Los demás casos de aceptación de Fase 4 permanecen cubiertos por sus checkpoints previos: cliente de servicio permitido, heartbeat ProcessAgent autorizado, cliente outside-root y rollback .98 a .97. Deben repetirse si cambian los componentes autenticados. Fase 5–10 siguen abiertas según la tabla actual del plan.
