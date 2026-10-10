# FG-350 — Aceptación del Host React instalado (.119 → .120) y cuatro correcciones

**Fecha:** 2026-10-09 local  
**Fase:** 8 (strict, Today/resumen, horarios, Preferencias, compacto) y 9 (aceptación instalada)  
**Estado:** .119 y .120 instalados por el propietario-presente con autorización explícita; .120 aceptado en el Host real.

## Qué se instaló y cómo

- Preflight (`invoke-installer-rollback-drill.ps1 -PreflightOnly`): instalado .118, sesión inactiva, Strict apagado, `InstallerInvoked=False`.
- `scripts/install-focusguard.ps1 -PackageDirectory <paquete> -ClientMetadataQueryConsent` desde un proceso **ya elevado** (sin automatizar UAC ni credenciales). Primero .119, después .120 (misma ruta y mismo consentimiento que la política instalada).
- Verificación posterior de .120: extensión 0.1.120; 7/7 ejecutables/DLL y 18/18 recursos UI idénticos al paquete; `FocusGuard` Running como `NT AUTHORITY\LocalService`; `ipc-policy.json` con el mismo owner SID y consentimiento; Desktop, ProcessAgent y NativeMessaging en sesión 1 como el usuario; `DrillClient status` → `serviceReady`, `extensionReady`, `Inactive`, Strict off.
- Setup .120: `artifacts/installer/FocusGuard-0.1.120-Setup.exe`, 442853560 bytes, SHA-256 `EA79115A528D5F2D874B56BA8661152CE4EA7D2168DC6C47C514171D59B55877`; ZIP 699174727 bytes, SHA-256 `533FD4FA21FE59D60ED24A59B088F54F2BB5F69E2A408A18C0444344DDEF047E`; payload 326386350 bytes, SHA-256 `721875AC7EDF846659BE45C029D640896BA97F3630A702A65E2C4EE7810C4331`. manifest 31/31; recursos UI del `dist` idénticos al payload; paquetes Chrome/Edge y `test-installer.ps1` pasan. Logs: `artifacts/fg349-install-0.1.119/install.log`, `artifacts/fg350-install-0.1.120/install.log`.

## Método de aceptación

El control de escritorio no puede operar `FocusGuard.UI.exe` (solo resuelve `FocusGuard` como el Desktop clásico). Se lanzó el Host instalado (`--pilot`) como propietario, con integridad media (RID 8192, sin elevación), mediante el helper del propio instalador (`FocusGuardInteractiveShell.ExecuteFromDesktop`) y la variable `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333`, solo en `127.0.0.1` y solo mientras duró la prueba. Un controlador CDP (Node, fuera del repositorio) leyó el DOM, emitió clics/teclas reales y capturó pantallas **de la aplicación instalada real contra el servicio real**. El Host se cerró al terminar (puerto cerrado, comprobado). No es una función del producto ni se añadió al repositorio.

Las sesiones usadas fueron solo temporizador, sin bloqueo, de 1–3 minutos, con el perfil existente `2`; no se activó ninguna sesión estricta.

## Hallazgos reales y correcciones (todos con prueba que falla antes y pasa después)

| # | Hallazgo en el Host instalado | Corrección | Aceptación en .120 |
| --- | --- | --- | --- |
| 1 | Preferencias mostraba «No se pudieron guardar las preferencias.» al abrirse: `loadPreferences()` se ejecuta al montar, antes del token del puente, y `BridgeUnavailable` no se reintentaba; el error quedaba pegado hasta pulsar Actualizar. **FG-347 había diagnosticado solo `RequestInProgress`.** | Reintento acotado (10 × 300 ms) para `RequestInProgress` y `BridgeUnavailable`; al abrir la pestaña se reintenta una lectura fallida; texto propio de fallo de lectura (`preferencesLoadError`) separado del de guardado; un fallo de lectura no altera la fase de guardado. | Primera apertura: 6 controles, sin alertas. |
| 2 | La revisión estricta mostraba el literal «{minutes}» en «espera mínima de {minutes} minutos». | Sustitución con `recoveryDelaySeconds / 60`; prueba que falla si queda cualquier `{…}` visible. | «…espera mínima de 15 minutos…». |
| 3 | Guardar la reflexión no daba confirmación (solo se deshabilitaba el botón). | Estado `role="status"` «Reflexión guardada en el diario local.», se limpia al editar. | Mensaje visible tras guardar. |
| 4 | Confirmar eliminar horario, y confirmar detener sesión, decían «Confirmar horario» / «Confirmar acción». | El botón nombra su acción («Eliminar horario», «Pausar», «Activar», «Detener sesión», «Saltar descanso»). | Verificado en los diálogos de eliminar y detener. |
| 5 | Un horario de **1 minuto nunca arranca**: la validación lo permite pero el servicio exige ≥ 1 min restante al reclamar y el tick (≤ 5 s) lo deja en 59 s → `Missed`. | Guarda en React: `min=2` y texto explicativo (`scheduleMinimumDuration`). **No se tocó el contrato ni el servicio**; ver límites. | `min="2"` y aviso presentes. |

Junto con FG-349 (centrado del diálogo estricto, foco, requisitos, caducidad), la suite pasa **131/131** en 22 archivos y `npm run build` (TypeScript, Vite, CSP, contraste, layout, reduced motion, contrato del diálogo estricto) pasa.

## Verificado en el Host instalado (nuevo)

- **Strict (sin activar):** el diálogo se centra (left 212,5 en 1200 px), desenfoque 6 px, animación `strict-review-in`, foco en el título en ambos pasos, requisitos con `aria-describedby`, caducidad visible, Esc cancela sin activar y el servicio sigue `Inactive`.
- **Sesión real normal (timer-only, 1–2 min):** revisión → inicio → cuenta atrás veraz («Confirmación de bloqueo: No solicitado») → expiración sin intervención → Actividad: cronología, resumen, objetivo, **reflexión persistida** (se restaura al reabrir), **Repetir** (borrador con 1 min, objetivo y bloqueo apagado; no inicia nada) → detener con revisión (`Stopped`).
- **Horarios reales:** crear, listar, pausar/eliminar con revisión y consecuencias. Un horario habilitado de 3 min con inicio automático **arrancó solo** a la hora (`Active`, 177 s; resultado «Iniciada»; próxima ocurrencia a una semana; historial «Sesión interrumpida» al detenerla). El de 1 min quedó `Missed` (hallazgo 5). Todos los horarios de prueba se eliminaron (`weekly-schedules.json` = `[]`).
- **Compacto:** 1200×760 → 480×340 → 480×220 (detalles plegados) → 1200×760; «Siempre visible» activa/desactiva el estilo `WS_EX_TOPMOST` real de la ventana y refleja `aria-pressed`.
- Con la ventana minimizada la página queda `hidden`, el sondeo se pausa, el estado pasa a «desactualizado» y los controles se deshabilitan; al restaurarla se recupera en ~2 s (comportamiento diseñado).
- Recorrido de Resumen, Perfiles, Actividad, Programación, Preferencias, Conexión y Ayuda sin marcadores sin sustituir ni desbordes horizontales.

Evidencia (sin el código de recuperación): `artifacts/fg350-host-acceptance/`. La captura del paso 1 de Strict mostraba un código (de una preparación cancelada, nunca activada); se eliminó y no se archivó.

## Bloqueo real y rendimiento con .120 instalado (nuevo)

- **Bloqueo de aplicación desde React:** perfil `gpt test` (regla `FocusGuard.TestTarget.exe` Block, sitio Allow), 2 min, bloqueo activado explícitamente. El servicio reportó `Active` y `enforcementConnected=true`. `FocusGuard.TestTarget.exe` lanzado como propietario (integridad media, sin elevación) desde el escritorio: el ProcessAgent lo cerró en **2,2 s** (segunda medición; la primera también lo cerró). El audit registra dos `Terminated` para la misma sesión y el resumen React muestra **«Intentos bloqueados: 2»**; el proceso no quedó vivo. La sesión expiró sola (`Expired`, `enforcementConnected=false`).
- **Rendimiento con sesión activa y bloqueo conectado (60 s, `measure-focusguard-resources.ps1 -Enforce`):** 11 procesos, 811,3 MB de working set, 439,0 MB privados, 0,40 % de CPU total del equipo. Service 77,9 MB WS / 39,6 MB privados / 0,06 %; ProcessAgent 51,6 MB / 21,8 MB / 0,29 %; Host UI 84,5 MB / 26,7 MB / 0,02 %; NativeMessaging 47,4 MB; Desktop clásico 197,1 MB WS / 147,0 MB privados / 0,03 %. Gates `serviceWithinBudget`, `singleDesktopInstance`, `singleReactHostInstance`: todos `true`. JSON: `artifacts/fg350-host-acceptance/resources-active-enforced-0.1.120.json`. Es una muestra de un minuto, no carga sostenida.
- No reproducido: la primera apertura de Actividad en un Host recién lanzado mostró datos anteriores a sesiones recientes (la ventana estaba minimizada/`hidden`); con la ventana visible, Actividad refleja una sesión recién terminada de inmediato (5 entradas a 2 s de expirar) y no se confirmó un fallo.
- Estado final del sistema: servicio `Running`/LocalService, `Expired`, Strict off, extensión lista, Host cerrado, ningún puerto de depuración abierto, `weekly-schedules.json` vacío. El historial local conserva las sesiones de prueba (aparecen en Actividad) y una reflexión de prueba.

## Límites y deuda registrada

- **Horario de 1 minuto (backend):** el contrato sigue aceptando 60 s; los horarios ya guardados de 1 min seguirán `Missed`. Decisión de propietario: subir el mínimo en el contrato (con migración de lo guardado, HIGH RISK) o permitir activación con el tiempo restante.
- **Sesión iniciada por horario:** no deja intención en el diario local, así que el Host muestra «Confirmación de bloqueo: Desconocido» y no ofrece objetivo/Repetir para esa sesión.
- **Copy duplicado:** en Programación y Preferencias el título de página y el de sección son idénticos y hay dos botones «Actualizar estado»; el local debería llamarse por lo que actualiza.
- La revisión normal tiene fecha «Revisión válida hasta», pero sin cuenta atrás como la estricta.
- **Sin cubrir:** notificaciones nativas con la UI cerrada y toast/bandeja (las posee el Desktop clásico; sin evidencia nueva), strict real activado (requiere ≥ 15 min de espera de recuperación), reboot/recovery, cambio real de monitor/DPI, Windows 11, Narrator, forced-colors reales, bloqueo web real en Chrome/Edge con .120, y el centrado de `.session-end-dialog` (tarea aparte).
- `FG-303`/Edge e instalación de la extensión Edge .120 siguen pendientes; Fase 10 no se inicia: **Avalonia se mantiene** (plan §22).

## Seguridad y reversión

Sin cambios de IPC, autorización, enforcement ni lógica de sesión. El puerto de depuración fue local, temporal y manual; no queda ninguno abierto. Reversión: reinstalar .118 desde `artifacts/installer/FocusGuard-0.1.118-Setup.exe` (rollback de instalador ya ensayado en FG-333) o retirar los cambios de `StrictReviewPanel`, `dashboard.ts#loadPreferences`, `PreferencesPanel`, `SessionHistoryDetail`, `ScheduleEditor`, `ActiveSessionControls` y las reglas finales de `tokens.css`.
