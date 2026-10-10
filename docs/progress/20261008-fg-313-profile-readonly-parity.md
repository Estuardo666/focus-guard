# FG-313 — Paridad de perfiles en Host .102

Fecha: 2026-10-08

Estado: completado; consulta de base de datos en modo solo lectura.

## Resultado

Se compararon las tarjetas de Perfil capturadas por UI Automation en FocusGuard UI .102 (modo `SOLO LECTURA`) con `%LocalAppData%\FocusGuard\profiles.db`. Los cinco nombres y recuentos coincidieron:

| Perfil | Reglas de aplicaciones | Reglas de sitios |
|---|---:|---:|
| 2 | 1 | 10 |
| Deep Work | 3 | 10 |
| Deep Work2 | 0 | 2 |
| gpt test | 1 | 1 |
| Social media | 0 | 7 |

En `gpt test`, la base conserva `FocusGuard.TestTarget.exe` (acción almacenada `1`, presentada por UIA como Bloquear) y `focusguard.invalid` (acción almacenada `0`, presentada como Permitir). UIA encontró deshabilitados los botones para quitar reglas y añadir sitio; la edición técnica no estaba habilitada. El selector quedó restaurado al perfil guardado `2`. No había sesión activa y no se escribieron perfiles.

## Evidencia y alcance

`artifacts/fg305-output/fg313-profile-readonly-0.1.102.json` contiene UIA, los registros consultados y la comparación validada. El primer JSON generado tenía campos SQLite nulos por un error en la captura; se reconstruyó desde la base en modo read-only y se verificó que los cinco recuentos coincidan antes de cerrar el hito.

Esto valida la representación instalada de perfiles y reglas en esta máquina y versión. No valida la edición, sincronización de datos, otros perfiles de Windows ni paridad con cada flujo clásico.
