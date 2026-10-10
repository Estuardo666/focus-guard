# FG-208 — Plan de replay exacto

## Objetivo

La acción Repeat debe cargar la intención aceptada de la sesión y preparar otra sesión con el mismo perfil, modalidad, valor de bloqueo, duración de timer y configuración de ciclo. Antes de iniciar, FocusGuard vuelve a cargar el perfil guardado y muestra la revisión normal/normal-cycle actual; el usuario debe confirmar de nuevo.

## Diseño de datos y seguridad

- Reutilizar `%LocalAppData%/FocusGuard/session-journal.json`, compartido con Classic. Añadir el valor `EnforcementEnabled` opcional: las entradas antiguas que no lo contengan son ambiguas y no se repetirán automáticamente.
- El Host registra intención sólo después de validar una respuesta `Accepted` del servicio, usando los datos del review que él mismo conservó, no valores reenviados por React.
- Un endpoint de lectura devuelve sólo metadatos acotados (sessionId, profileId/name, modalidad, duración, consentimientos y configuración de ciclo) más un resumen del perfil actualmente disponible. No expone rutas/reglas ni reflection/objective.
- La configuración de ciclo se guarda como valores y se rehidrata como edición personalizada para no cambiar silenciosamente si luego cambió o desapareció el preset referenciado.
- Perfil eliminado, configuración inválida, enforcement desconocido en journal legacy o ciclo sin configuración exacta deshabilitan Repeat. Las sesiones strict requieren su propio flujo strict, aún no migrado a React.
- La escritura compartida del journal debe releer/combinar bajo exclusión entre procesos para no perder una entrada cuando Classic y Host conviven.

## Verificación prevista

Compilar UI estricta, solución Release y generar el instalador versionado con verificación de recursos incrustados. No ejecutar tests durante esta continuación. La implementación no instala FocusGuard ni cambia el servicio, agentes, perfiles o estado de sesión.

## Resultado FG-208

Implementado para sesiones no strict. Las sesiones React se escriben en el mismo journal sólo después de que el Host valida que el servicio aceptó el arranque; el Host lee el profileId vigente y devuelve un DTO pequeño sin reglas. El repeat restaura el profileId/modo/minutos/enforcement y la configuración original del ciclo, y vuelve a la preparación/revisión ordinaria. La revisión informa que carga las reglas actuales del perfil. Datos legacy sin enforcement explícito, perfil eliminado, ciclo incompleto y sesiones strict deshabilitan la repetición. El historial no guarda snapshots de URLs, ejecutables ni reglas.

El chunk de detalle de sesión se separó como `SessionHistoryDetail.js` para mantener `host.js` bajo500 kB y reducir la superficie cargada al abrir Activity/Closure. Se añadió a la allowlist de recursos WebView2, payload del Host, verificación dist y validación de archivos requeridos por Setup. La lectura explícita del journal usa el mismo lock de archivo que las escrituras para no competir con el reemplazo atómico.

Validación: `npm run build` en D y E pasó (TypeScript estricto, Vite, chunks locales y CSP/resource verifier; `host.js`499.61 kB); `dotnet build FocusGuard.slnx --configuration Release --no-restore` pasó con0 warnings/0 errors. `graphify update .` terminó: 9,785 nodos/23,498 aristas/978 comunidades; omitió `graph.html` al exceder su límite de visualización, y actualizó `graph.json`/`GRAPH_REPORT.md`. El builder0.1.57 completó publicación y comprobación de hashes de los recursos incrustados. El ZIP contiene `SessionHistoryDetail.js`.

Instalador: `artifacts/fg208/FocusGuard-0.1.57-Setup.exe`, SHA256 `B164B598AA6E32683A15CAA165718C62935766D74B37C496FFEBE7F888831EB0`.

ZIP de menú: `artifacts/fg208/FocusGuard-0.1.57-win-x64-menu.zip`, SHA256 `6C88DEF77F1184BDF51764C67B60AE1103F8F67E16FC4142012123D727929A2E`.

No tests ejecutados ni producto instalado. Strict/recovery, snapshot histórico de reglas, entrada legacy sin consentimiento, política de descanso y aceptación instalada permanecen pendientes.
