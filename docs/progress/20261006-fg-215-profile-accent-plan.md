# FG-215 — Plan: acento de perfil en dashboard React

## Objetivo

Usar el `AccentHex` que ya guarda cada perfil para identificar el perfil seleccionado/activo y su marca de progreso en el nuevo dashboard. El tema claro/oscuro permanece compartido por toda la app.

## Contrato visual

- Seguir UX_SPEC: el acento identifica al perfil/progreso, no repinta advertencias ni indicadores de estado.
- Pasar el campo opcional `accentHex` por los resúmenes ya existentes de `profiles.list` y `session.intent.get`. Validar exactamente `null` o `#RRGGBB` en Host/React; los datos sin valor usan el mint semántico vigente.
- Exponerlo mediante `--profile-accent`, acotado a un punto/indicador de identidad y timeline, nunca como `--accent` global. Botones primarios, focus ring y warning/error siguen en tokens semánticos.
- No añadir nuevos controles ni modificar persistencia/edición de color: ProfileEditor ya cambia `AccentHex` con CAS.
- Continuar la dirección graphite/mint/Geist existente. Visual QA contrasta al menos perfiles con null, tono oscuro y tono claro en dark/light y forced colors.

## Verificación

- Pruebas DTO C# y TypeScript para hex válido/null/rechazo de strings inválidos.
- Prueba visual UI de que el acento sólo afecta a marcadores y que warning/CTA no heredan el acento de perfil.
- Suites UI y Host, Release solution, Graphify, build/CSP y package0.1.64. Sin instalación.
