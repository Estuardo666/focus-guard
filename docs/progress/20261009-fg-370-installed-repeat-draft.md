# FG-370 — Repetir desde el Host instalado solo prepara un borrador

## Objetivo

Verificar en el Host .123 que la acción Repetir de un resumen completado prepara la configuración para revisión, sin iniciar automáticamente otra sesión.

## Prueba y resultado

- Diagnostic antes de la acción: `Expired`, SessionId `0c695082-5a94-4ca6-8cf6-e0a2fa5d227f`, ServiceReady/ExtensionReady true, Strict=false y `enforcementConnected=false`.
- En el resumen de FG-368, activé `Revisar y repetir esta configuración`. El formulario volvió como borrador `gpt test`, duración 1 min, bloqueo activado y Strict apagado; el siguiente botón siguió siendo `Revisar sesión`. No apareció la confirmación de sesión ni se envió una activación.
- Diagnostic después: el mismo estado `Expired` y SessionId. `desktop-settings.json` conservó el SHA-256 original `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55`.
- Cerré y reabrí el Host .123; cargó de nuevo el perfil guardado `2`, el ciclo `Enfoque profundo` y las preferencias previas. No se modificaron reglas.

Evidencia: `artifacts/fg370-output/installed-repeat-draft-readonly.json`.
