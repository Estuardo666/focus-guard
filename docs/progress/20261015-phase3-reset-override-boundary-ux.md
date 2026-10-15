# Fase 3: aclarar la prioridad de overrides en el reset compartido

## Resultado

La confirmación nativa de Windows y la revisión de conflicto del reset ahora advierten que el reset cambia solo los valores compartidos y que los overrides «Kept on this PC / Se conserva en esta PC» siguen activos y tienen prioridad hasta retirarlos. Esto coincide con la precedencia especificada y evita sugerir que el equipo mostrará defaults locales mientras haya excepciones activas.

## Cambios

- Copia EN/ES en `HostStrings.resx` para la confirmación de reset nativa.
- Copia EN/ES en `AccountPanel` para el límite de reset/conflicto.
- `AccountPanel.test.tsx` comprueba las dos traducciones; `HostWindowStringsTests` comprueba que los recursos nativos contienen la advertencia y que el detalle con valores se formatea correctamente.
- Candidato distribuible `.180`; no se instaló ni desplegó.

## Verificación

- UI: **254/254 PASS** y TypeScript/build PASS.
- Host: **261/261 PASS**.
- Formato y graphify: PASS.
- Guard de producción: PASS.
- `scripts/test-installer.ps1`: PASS (8 casos de readiness y contratos).
- `--package-self-test`: PASS.
- SHA-256 Setup `.180`: `B8DDFA39BD5CFF8F2AC5E9102A2E60BBF0CA56DF961D1A18735C1ED666BF7AA4`.
- SHA-256 ZIP `.180`: `B3D7C4BEBB367F1CA5035610B928D5AB013E005D2FCA5D3B4B0D3393E5B79620`.

## Límites

La aceptación física de overrides/reset offline en dos PCs y la validación de staging siguen pendientes. Esta aclaración de UX no demuestra recepción física del tombstone ni cambio de valores entre equipos.
