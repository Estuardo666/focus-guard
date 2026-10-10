# FG-364 — Sonda autenticada del Desktop instalado

## Plan

1. Confirmar Diagnostic status y SessionState antes de la prueba.
2. Ejecutar C:\Program Files\FocusGuard\FocusGuard.Desktop.exe --service-health-check desde el Desktop instalado, en el token actual del propietario.
3. Confirmar exit 0 y que los ocho status probes reportan ServiceReady.
4. Consultar Diagnostic status después y verificar que SessionId, Strict y Enforcement no cambian.

## Seguridad e impacto

La rama ServiceHealthCheck de Desktop crea un cliente con el rol Desktop y solo llama a GetStatusAsync. No abre una ventana, inicia una sesión, cambia políticas ni escribe perfiles. El servicio permanece como LocalService.

## Resultado

Diagnostic .123 antes y después: exit 0, SessionState=Expired, mismo SessionId ed3f69ed-5e08-4264-b65a-3c0542d19e31, ServiceReady=true, ExtensionReady=true, Strict=false y enforcementConnected=false.

FocusGuard.Desktop.exe --service-health-check se ejecutó desde C:\Program Files\FocusGuard con el token del propietario. Exit 0; health-check.json informó Stage=Ready y Failure=null después de sus ocho consultas autenticadas. FocusGuard Service continuó Running como LocalService.

El probe actualiza LocalAppData\FocusGuard\diagnostics\health-check.json. Se guardó el contenido previo en artifacts/fg364-output/health-check-before.json; tras capturar el resultado nuevo en installed-desktop-health.json, el original se restauró y se verificó byte por byte con SHA-256 2E21DF96BD0C558206671A121938444F489676A313AE378C9F6064EDD66C606D.

## Alcance y siguiente paso

FG364 confirma un cliente Desktop permitido en .123. No cierra la matriz positiva/negativa de todos los roles ni la aceptación del Host .128. Siguen pendientes la prueba dedicada del ACK, Edge, el reboot/Strict y la recuperación completa.
