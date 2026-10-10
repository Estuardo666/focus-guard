# FG-366 — Auditoría de manifiesto IPC instalado .123

## Objetivo

Revalidar en Program Files la política IPC protegida de la versión instalada .123: propietario, roles, hashes de ejecutables y bibliotecas gestionadas, y cuenta del servicio.

## Comprobaciones

- `ipc-policy.json` usa schema 1, apunta a `C:\Program Files\FocusGuard` y su `ownerUserSid` coincide con el usuario actual. El SID exacto no se guardó en la evidencia.
- El DACL de `ipc-policy.json` está protegido. SYSTEM y Administradores conservan escritura; LocalService y Usuarios tienen solo lectura/ejecución. No se encontraron ACEs Allow con escritura para principals no confiables.
- Los cinco roles presentes son Desktop, ProcessAgent, NativeMessaging, WebViewHost y Diagnostic.
- Se cotejaron SHA-256 de los seis ejecutables (Service y cinco roles) y sus seis DLL gestionadas contra el propio `ipc-policy.json`: 12/12 coinciden. Ninguna ruta sale de Program Files ni es reparse point.
- El servicio `FocusGuard` permanece Running como `NT AUTHORITY\LocalService`.

## Resultado y límites

Todas las comprobaciones pasan. Fue una inspección read-only; no cambió configuración ni estado de máquina. Confirma la integridad local de esta política y sus archivos registrados, pero no completa la matriz de autorización positiva/negativa, el ACK dedicado, el bloqueo real en Edge ni el reboot drill.

Evidencia: `artifacts/fg366-output/installed-ipc-policy-hash-audit.json`.
