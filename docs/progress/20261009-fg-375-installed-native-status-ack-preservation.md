# FG-375 — `status` Native Messaging conserva el ACK instalado .123

## Objetivo

Reproducir con el NativeMessaging y Service instalados el caso de FG-337: una consulta `status` sin ACK no debe borrar la confirmación browser de una política vigente.

## Prueba y resultado

- Inicié una sesión normal de 1 minuto con `gpt test`, bloqueo activado, una aplicación bloqueada y cero sitios bloqueados. La política browser incluía la regla guardada `focusguard.invalid` (Allow). Antes de la consulta, el Host mostró `Confirmada`.
- El `FocusGuard.NativeMessaging.exe` instalado .123 aceptó el origen exacto del manifiesto. Envié una trama `status` correlacionada con `acknowledgedPolicyId/hash=null`, sin URL, heartbeat, audit ni capture.
- El host salió 0 y devolvió `healthStatus`, ServiceReady/ExtensionReady true, SessionState `Active`, política presente y `EnforcementConfirmed=true`. El Host continuó mostrando `Confirmada` después.
- Diagnostic post-consulta mantuvo la sesión activa y los canales Ready; la sesión expiró naturalmente poco después. Strict=false y TestTarget no se ejecutó.
- Restauré preferencias al SHA-256 original y reabrí el Host.

El primer flag `Passed=false` del arnés comparaba el SessionId con un GUID placeholder; esa aserción era inválida. Se corrigió el veredicto a partir de los campos de respuesta capturados, sin afirmar una igualdad de SessionId que el arnés no guardó antes de la consulta.

Evidencia: `artifacts/fg375-output/installed-native-status-ack-preservation.json`.
