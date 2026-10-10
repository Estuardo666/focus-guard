# FG-321 — Distinguir bloqueo solicitado de confirmación pendiente

Fecha: 2026-10-08
Estado: implementado y validado en fuente/paquete .109; aceptación instalada pendiente.

## Problema

La tarjeta Protección usa `enforcementConnected=false` para mostrar “Sin confirmación” en cualquier sesión activa. Un timer que explícitamente no pidió bloquear aplicaciones ni sitios queda presentado como si esperara confirmación.

## Decisión y plan

1. Leer el registro existente `session.intent.get` sólo durante Active/Activating y tomarlo como actual únicamente si su SessionId coincide exactamente con el status fresco.
2. Si la intención de esa misma sesión dice `enforcementEnabled=false`, mostrar “No solicitado” y explicación localizada. Si dice `true`, seguir reflejando `enforcementConnected`; si es `null`, falta o el status es stale, conservar la salida conservadora/desconocida.
3. No deducir consentimiento de un perfil, `extensionReady` ni del proceso Agent; no tocar backend, IPC, políticas, sesión o reglas.
4. Añadir pruebas ES/EN para no solicitado, pendiente, confirmado, terminal y desconocido, más el guard de sesión.
5. Ejecutar build/test, suite relevante, `graphify update .` y regenerar un Setup versionado .109 sin instalarlo.

## Aceptación

La tarjeta no llama “Sin confirmación” a una sesión cuyo consentimiento explícito es false; una intención perteneciente a otro SessionId nunca altera el status actual; unavailable/stale no presentan valores afirmativos obsoletos.

## Implementación

- `App` observa el store del Dashboard y solicita `session.intent.get` si hay una sesión Active/Activating, el status está fresco y el intent de ese SessionId no se ha cargado. La vista Resumen ya tiene su carga propia; las demás vistas comparten la lectura sin repetirla por cada refresh.
- `protectionFactFor` exige coincidencia case-insensitive del SessionId. Sólo `enforcementEnabled === false` produce la etiqueta nueva “No solicitado” y su explicación; `true` continúa leyendo `enforcementConnected`, y null/mismatch conserva “Sin confirmación” si el canal no confirmó. Inactive y terminal conservan “Sin sesión activa”; un status stale no muestra facts.
- ES/EN agregan dos claves localizadas. No se cambió el backend, la policy, el agente ni el comportamiento de sesiones.
- El build .109 empaqueta también FG-308/319/320. Manifiesto31/31, UI18/18 hashes, Setup/ZIP hashes y paquetes Chrome/Edge verificados; los cuatro Host smokes salen0.

## Verificación

- React118/118; pruebas nuevas cubren consentimiento false, true pendiente, channel confirmed, consentimiento null, SessionId que no coincide, lectura stale, estado preloaded y flujo App→Dashboard `session.intent.get` desde Conexión por pipe.
- `dotnet test FocusGuard.slnx -c Release --no-restore`: 877/877 en14 assemblies; TypeScript/Vite/contraste/CSP/layout también pasan.
- Extensión19/19; tests de installer y Chrome/Edge packages; Host smokes local, runtime ausente, renderer recovery y renderer crash: exit0.
- Recibo: `artifacts/fg321-build-receipt.json`.

.109 no está instalado. La lectura de consentimientos queda probada con tests de fuente; la captura visual de la tarjeta activa y la continuidad real siguen pendientes hasta una actualización owner-present.
