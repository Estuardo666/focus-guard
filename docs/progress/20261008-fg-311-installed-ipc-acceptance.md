# FG-311 — IPC permitido y rechazo bajo Service LocalService

Fecha: 2026-10-08
Estado: par permitido/rechazado verificado en la instalación .102; matriz completa y rollback siguen abiertos.

## Prueba instalada

El binario FocusGuard.DrillClient.exe instalado en Program Files, cuya identidad está incluida en ipc-policy.json, envió un StatusRequest como cliente Diagnostic. El servicio real respondió serviceReady=true, extensionReady=true y sessionState=Expired. clientMetadataQueryConsent=true y el manifiesto de extensión declara .102.

Luego el mismo cliente Diagnostic envió un heartbeat al endpoint ProcessAgent. El endpoint cerró la conexión con DENIED_EOF; el DrillClient consideró el rechazo esperado y terminó con exit0. Esto prueba una operación Diagnostic permitida y una función Agent denegada a Diagnostic.

Después de ambos probes, FocusGuard Service permaneció Running como NT AUTHORITY\LocalService, PID25096. El estado de sesión y SessionId siguió en Expired; no se inició ni detuvo ninguna sesión. El Host normal siguió reportando Service conectado, extensión disponible y sin sesión activa.

## Alcance pendiente

Esta evidencia cubre la pareja Status permitido/ProcessAgent denegado. Aún faltan pruebas vivas para los otros roles autorizados/no autorizados y el rollback instalado .102. FG-308 también registra que el Setup dejó Desktop/ProcessAgent con token elevado; el runtime actual fue relanzado en medium, pero la causa del launcher sigue bajo investigación.

## Evidencia

artifacts/fg305-output/fg311-installed-ipc-0.1.102.json
