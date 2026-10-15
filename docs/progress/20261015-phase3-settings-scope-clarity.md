# Fase 3: precisar qué significa “tema” en la sincronización

## Resultado

La sincronización y las transferencias de ajustes ahora nombran el campo compartido como **modo claro/oscuro**. La paleta Bruma/Mist u Océano/Ocean, el estilo del orbe y las demás preferencias se identifican como locales. El contrato de tres campos no cambió.

## Cambios

- Textos EN/ES de la tarjeta Conexión, transferencia única, archivo portátil, consentimiento, reset y excepciones locales.
- Diálogos nativos de Windows actualizados para consentimiento, transferencia, archivo, reset e overrides.
- `HostWindowStringsTests` comprueba en ambos idiomas que los diálogos nombran el modo y dejan explícitos paleta/orbe locales.
- `AccountPanel.test.tsx` verifica la terminología de modo claro/oscuro y los ejemplos localizados de paleta.
- Runbook y matriz de aceptación ahora registran modo compartido y paleta/orbe por separado.
- `App.test.tsx` fija Bruma (`mist`) y estilo `lava`, procesa el refresco remoto de modo/idioma/duración y comprueba que `data-palette`, `data-orb` y `localStorage` no cambian, sin `preferences.set` de eco.

## Verificación

- UI: **254/254 PASS**; TypeScript/build y verificaciones CSP/contraste/responsive PASS.
- Regresión de refresco en ventana abierta: modo/idioma/duración remotos aplicados y paleta/orbe locales conservados.
- Host Windows: **263/263 PASS**.
- `dotnet format whitespace ... --verify-no-changes`: PASS.
- Build del Setup/ZIP `.182`: PASS; guard de Client de producción PASS.
- `scripts/test-installer.ps1`: PASS, incluidos 8 casos de reintento de readiness.
- `--package-self-test`: exit 0.
- SHA-256 Setup: `0A390D45ED48B036FAFD25FE3A9A6F9205F47D64E17716B07DC29DC45438BAA8`.
- SHA-256 ZIP menú: `46600F7E5263E6AE434524ADADAE5AFCF57EF0FE3937065ACAB312D47B44C84C`.
- Graphify: PASS; 1.932 archivos AST, 24.804 nodos, 181.235 aristas y 2.028 comunidades.

## Límites

No se añadieron paletas ni estilos del orbe al documento E2EE; siguen locales conforme al comportamiento actual. `.182` se generó y validó, pero no se instaló. Aún falta la PC hermana para confirmar físicamente la independencia de paleta/orbe y los escenarios restantes del runbook. Staging permanece detrás de esa aceptación.
