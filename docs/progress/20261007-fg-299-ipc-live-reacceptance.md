# FG-299 — Revalidación IPC instalada de Fase 4

Fecha: 2026-10-07
Estado: aceptado sobre FocusGuard .100 instalado.

## Prueba
El FocusGuard.DrillClient.exe instalado ejecutó una consulta Diagnostic status autorizada y recibió serviceReady=true, extensionReady=true, sessionState=Inactive. Inmediatamente después, probe-agent-role-denied envió un heartbeat al endpoint ProcessAgent usando el rol Diagnostic; el resultado fue DENIED_EOF con código0, que es el rechazo esperado.

El servicio conservó PID5704 bajo NT AUTHORITY\LocalService antes/después. Una segunda consulta mostró la misma sesión Inactive y sin SessionId. No se inició una sesión, no se cambió perfil y no se modificó servicio ni ACL.

## Evidencia
artifacts/fg298-output/fg298-ipc-live-acceptance.json

## Alcance
Esto confirma otra vez la distinción entre rol permitido y función no autorizada en la instalación .100. No cambia la versión instalada y no cubre por sí mismo cambios de identidad del paquete .101 ni rollback. FG-285 conserva la prueba owner-present del rollback .98→.97.
