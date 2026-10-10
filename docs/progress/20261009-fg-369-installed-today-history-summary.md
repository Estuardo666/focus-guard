# FG-369 — Today e historial de sesión instalados .123

## Objetivo

Verificar en el Host instalado que una sesión de prueba completada aparece en Today y que abre su resumen sin iniciar otra sesión.

## Prueba y resultado

- Today, 9 de octubre de 2026, mostró el registro `gpt test · 12:49–12:50 p. m. · 1 min · Sesión completada` creado por FG-368.
- Abrí `Ver resumen`. El Host indicó sesión completada, perfil `gpt test`, 1 minuto de enfoque, 0 intentos bloqueados, racha de 4, sin reflexión ni notas de bloqueo guardadas.
- La acción Repetir estaba disponible, pero no se activó. Cerré el resumen y devolví el Host a Resumen.
- Diagnostic se mantuvo `Expired`, mismo SessionId, ServiceReady/ExtensionReady true, Strict=false y `enforcementConnected=false`. No se modificaron reglas ni preferencias.

Evidencia: `artifacts/fg369-output/installed-today-session-summary-readonly.json`.
