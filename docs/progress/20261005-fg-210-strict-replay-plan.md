# FG-210 — Replay strict con review nuevo

## Objetivo

Permitir que el usuario repita una sesión strict aceptada desde historial. La repetición restaura configuración como borrador y requiere una nueva preparación, código de recuperación y revisión strict completa.

## Diseño

- El journal sigue siendo metadato local sin autoridad, secreto de recuperación ni snapshot de reglas. Sólo se reproduce una entrada strict guardada tras aceptación confirmada por el servicio.
- La intención es reproducible si se encuentra el perfil actual, existe consentimiento explícito de bloqueo y la configuración de ciclo guardada es válida. El review mostrará las reglas actuales y el servicio volverá a validarlas.
- React restaura la configuración strict y usa `session.strict.prepare`; no reutiliza preparación, código o confirmación originales y nunca inicia de forma automática.
- El flujo strict conserva sus límites: delay de recuperación 15 min, código nuevo, guardado/confirmaciones antes de activar, operación de un solo uso y Stop/Skip no disponible.
- No cambiar ni convertir las reglas de descanso. Si la configuración actual no pasa validación, el review falla con el motivo existente.

## Validación

- Pruebas Host: intención strict reproducible para timer/ciclo, consentimiento legacy desconocido no reproducible y perfil ausente no reproducible.
- Pruebas React: contrato acepta `strict: true`; repeat restaura la forma y prepara con `session.strict.prepare`, no con el path normal ni con un start directo.
- Ejecutar suite UI, suite Host, build de producción/CSP/chunks y Release solution. Crear artefactos 0.1.59 en rutas nuevas, verificar hashes y recursos embebidos; no instalar.

## Resultado

El Host ya no rechaza automáticamente una intención sólo porque la sesión anterior fuera strict. La hace reproducible únicamente cuando encuentra el perfil vigente, el consentimiento de enforcement no es legacy/desconocido y los valores de ciclo guardados son válidos. La proyección permanece acotada y sin rutas o credenciales.

React repone la configuración como borrador y conserva el modo strict. Un aviso explica que se necesita otra review y código de recuperación; al preparar se llama al protocolo strict existente, que genera un código nuevo y vuelve a revisar el perfil/política actuales. El control nunca activa automáticamente. Si el usuario cambia el modo o la configuración, la operación pasa a ser una nueva sesión normal de su elección, sujeta a la review normal correspondiente.

## Validación ejecutada

- `npm test`: 13 archivos, 69 pruebas aprobadas.
- `npm run build`: TypeScript, Vite, CSP, los 10 chunks y las fuentes locales aprobados.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 133 pruebas aprobadas, incluidas replay strict timer/ciclo, consentimiento legacy y perfil eliminado.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings y 0 errors.
- `graphify update .`: 9,880 nodos, 25,016 aristas y 971 comunidades; se actualizaron `graph.json`/`GRAPH_REPORT.md`, sin HTML por el límite visual.
- Builder 0.1.59: los 29 recursos declarados y los diez chunks requeridos están presentes; el ZIP menú contiene 1,730 entradas. El builder comparó los hashes de los recursos Host/UI incrustados con el publish, y el Setup del ZIP coincide con el standalone.

Setup autónomo (`D:\TEMP\focusguard-fg166-validation\artifacts\fg210\FocusGuard-0.1.59-Setup.exe`): SHA-256 `D2A0EFE68E150E4B8AFCC5A145AECBF3100464B45E4651D875B1B48513EC9FEC`.

ZIP menú (`D:\TEMP\focusguard-fg166-validation\artifacts\fg210\FocusGuard-0.1.59-win-x64-menu.zip`): SHA-256 `B57853C912471AAD5B98B8E37B0D1D22A80B8E9B47654902D51F47042E1DEB12`.

No se instaló. Sin cambios en el servicio activo, reglas del usuario ni sesiones. La semántica de reglas Block de descanso y la aceptación instalada quedan abiertas.
