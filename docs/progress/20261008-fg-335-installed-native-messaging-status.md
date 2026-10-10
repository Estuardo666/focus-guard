# FG-335 - Native Messaging instalado hacia el servicio

**Fecha:** 2026-10-08
**Fase:** 4, evidencia parcial de 7
**Estado:** consulta de estado instalada aceptada; bloqueo real de navegador no probado.

## Evidencia

- La extensión Chrome de la sesión actual tiene registrada la ruta `C:\Program Files\FocusGuard\native-messaging\chrome.json`.
- Durante la observación, Chrome abrió el `FocusGuard.NativeMessaging.exe` instalado. La cadena de procesos fue `chrome.exe` → `cmd.exe` → NativeMessaging PID 21372; el argumento de origen coincidió exactamente con el manifiesto instalado: `chrome-extension://emhdinkjpinjnmldipnejnbeefgonjnj/`.
- Una segunda ejecución, usando el NativeMessaging instalado y ese origen permitido, envió un frame de `status` con correlación nueva. El Service contestó `healthStatus`, correlación coincidente, `serviceReady=true`, `extensionReady=true`, `sessionState=Inactive`; su cuenta SCM es `NT AUTHORITY\LocalService`.
- Una ejecución separada con origen no registrado terminó con código 3 antes de abrir el pipe.
- El `DrillClient status` anterior y posterior permanece `Inactive`, sin SessionId y `strictMode=false`. No se visitó ningún sitio, no se cambió ningún perfil y no se activó enforcement.
- Durante otra muestra, seis respuestas `status` separadas por dos segundos conservaron `extensionReady=true` y no incluyeron policy. `ExtensionHealthPipeService` solo llama a `RecordHeartbeat` para `heartbeat` y calcula frescura en cinco segundos; `native-health.js` envía heartbeats cada dos segundos. Como `status` no renueva el lease y todas las respuestas tenían `policy=null`, no había una entrega activa cuya confirmación pudiera borrarse. Junto al proceso NativeMessaging que Chrome inició con el origen registrado, esto es evidencia de heartbeats recientes del navegador, aunque el Service no asocia el lease a un PID o correlación de heartbeat.

Recibos: `artifacts/fg335-output/fg335-installed-native-messaging-status.json` y `artifacts/fg335-output/fg335-chrome-heartbeat-freshness-10s.json`.

## Límite de esta prueba

El frame `status` y las seis muestras se enviaron desde ejecuciones directas del binario instalado; no se leyó el tráfico del proceso NativeMessaging que mantiene Chrome abierto. La frescura del lease más ese proceso vivo respalda la inferencia de un heartbeat reciente, pero no demuestra navegación bloqueada ni aceptación en Edge. Tampoco cubre el handshake positivo de ProcessAgent durante una política activa; Fase 4 sigue abierta.
