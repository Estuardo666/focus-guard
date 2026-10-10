# FG-208 — Replay de configuración de sesión en React

## Alcance implementado

- Classic y FocusGuard.UI comparten el journal existente en el perfil del usuario. Las lecturas normales quedan cacheadas; la lectura explícita para Repeat recarga el journal, y cada escritura sincroniza por archivo, fusiona la última versión y usa un temporal único para no perder entradas de la otra UI.
- Se guardan `EnforcementEnabled` y la configuración aceptada. Es nullable para distinguir sesiones antiguas sin consentimiento conocido de sesiones nuevas con bloqueo desactivado.
- El Host registra una intención sólo después de validar `Accepted`, `sessionId`, modalidad y límites horarios del servicio. El navegador React no puede inventar el registro aceptado.
- El Host responde el replay con un DTO limitado: perfil actual/resumen de conteos, ID original, modalidad, duración, consentimiento, strict y configuración del ciclo. No devuelve rutas, patrones ni reglas.
- Repeat bloquea perfiles ausentes, datos legacy con enforcement desconocido, configuración de ciclo ausente/inválida y sesión strict. Las sesiones no strict recuperan esos valores y vuelven al formulario más el review fresco antes de activar.
- El review vuelve a cargar las reglas **actuales** del profileId guardado y enseña sus conteos. El producto no conserva snapshots históricos de reglas en el journal ni pretende repetir una política ya modificada.
- `SessionHistoryDetail` pasó a un chunk local diferido de 5.43 kB. `host.js` quedó en 499.61 kB y el empaquetador/Setup/WebView allowlist requieren y verifican ese recurso.

## Validación

- `npm run build` en D y E: TypeScript estricto, Vite, verificador de chunks/CSP; pasó sin warning de tamaño.
- `dotnet build FocusGuard.slnx --configuration Release --no-restore`: pasó con 0 advertencias y 0 errores. Compiló los proyectos de test, pero no ejecutó suites.
- `graphify update .`: terminó con 9,785 nodos, 23,498 aristas y 978 comunidades. No generó `graph.html` porque el grafo excede el límite de visualización; actualizó `graph.json` y `GRAPH_REPORT.md`.
- `scripts/build-installer.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.1.57`: terminó y comparó hashes de Desktop, host React, HTML/CSS y los cuatro chunks locales incrustados. El ZIP contiene `SessionHistoryDetail.js`.
- Instalador standalone SHA256: `B164B598AA6E32683A15CAA165718C62935766D74B37C496FFEBE7F888831EB0`.
- ZIP menú SHA256: `6C88DEF77F1184BDF51764C67B60AE1103F8F67E16FC4142012123D727929A2E`.
- Paquete 0.1.56 generado, no instalado. No se alteraron servicio, agente, perfiles, estado del usuario ni sesiones.

## Pendiente

Strict/recovery y repetición strict con review nuevo; reconstrucción exacta de reglas históricas; journal legacy sin consentimiento conocido; decisión sobre Allow/Block en descansos; prueba instalada de Fases 4–7 y 9; demás paridad Phase8 (temas por perfil, help, compact/tray y notificaciones cuando el host está cerrado). La política del perfil se reevalúa desde su versión actual durante cada nueva revisión.
