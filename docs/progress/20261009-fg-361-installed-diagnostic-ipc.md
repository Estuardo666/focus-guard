# FG-361 — Prueba instalada de Diagnostic frente a LocalService

## Plan

1. Invocar el Diagnostic instalado desde C:\Program Files\FocusGuard y guardar su consulta autenticada de estado.
2. Invocar desde ese mismo cliente el endpoint de ProcessAgent; aceptar solo el rechazo esperado DENIED_EOF.
3. Consultar el estado otra vez y confirmar que Service/SessionState no cambió.

## Límites

Las operaciones serán de solo lectura y no iniciarán sesiones, cambiarán perfiles ni alterarán horarios. La prueba cubre un cliente permitido y una pareja Diagnostic→ProcessAgent denegada; no representa la matriz completa de Fase 4 ni la continuidad de .128. Si la respuesta no coincide con el contrato, se detendrá el procedimiento y se documentará el fallo.

## Resultado

Diagnostic .123 se ejecutó desde C:\Program Files\FocusGuard y completó status con exit 0; serviceReady y extensionReady fueron true. probe-agent-role-denied terminó con exit 0 y DENIED_EOF, que es el rechazo esperado del endpoint ProcessAgent. Antes y después: SessionState Inactive, SessionId null, Strict false, enforcementConnected false; status volvió a exit 0. Evidencia JSON: artifacts/fg361-output/installed-diagnostic-role-probe.json.

## Alcance y siguiente paso

Esto confirma una operación permitida y una denegación real bajo el servicio instalado como LocalService. No cubre la matriz de los cinco roles, el handshake Agent con una política activa, el ACK Native Messaging ni rollback. .128 sigue sin instalarse; la siguiente validación de Fase 4 será ejecutar el handshake Agent independiente durante una sesión normal en el Host instalado, y luego completar la aceptación funcional .128.
