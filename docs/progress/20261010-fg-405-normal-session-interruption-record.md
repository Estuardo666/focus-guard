# FG-405 — registro y aviso de sesión normal interrumpida

## Contexto y plan

FG-404 demostró en .135 que reiniciar el servicio con una sesión normal activa la pierde sin aviso y sin registro. El propietario eligió la opción (a): mantener «normal no persiste» (`SPEC.md`), pero registrar la interrupción y avisarla en el Host. Corte vertical mínimo: servicio → historial/audit → Host React.

## Cambios

- `FocusGuard.Contracts/ActivityContracts.cs`: `FocusAuditEventKind.SessionInterrupted`, añadido al final (compatible con el JSONL existente y con Desktop, que usa la etiqueta genérica).
- `IFocusService.InterruptNormalSessionForShutdownAsync` (implementación por defecto no-op) e implementación en `InMemoryFocusService`: una sesión normal Active/Activating pasa a Stopped, se registra en el historial como no completada (minutos reales de enfoque), se escribe el audit `SessionInterrupted` con su SessionId y se libera la política. Las sesiones Strict no se tocan.
- `NormalSessionShutdownRecorder` (`IHostedService`), registrado el último en `Program.cs` para que el host lo detenga primero, con un tope de 5 s; los fallos van a `ServiceFailureLog`.
- Host React (`App.tsx`): una ref sobrevive a las fases stale/error. Si se vio la sesión en curso y el servicio vuelve `Inactive` sin SessionId antes de su EndUtc, aparece un aviso persistente `role="alert"` con «Ver actividad» y «Cerrar». Stop y Expired conservan el SessionId, así que no lo disparan. Textos en `locales/es.json`/`en.json`; estilos con tokens (`--warning`), apilado con `.phase-notice` y forced-colors.
- `SPEC.md` y `THREAT_MODEL.md` documentan el comportamiento y la limitación del crash.
- Versión 0.1.136 (UI, extensión).

## Pruebas añadidas

- `NormalSessionShutdownTests` (3): sesión activa registrada como Stopped con planificados/enfocados y audit; sin sesión no escribe nada; el recorder interrumpe al detenerse el host.
- `InMemoryFocusServiceTests.ShutdownInterruptionLeavesStrictSessionToItsSnapshot`.
- `App.test.tsx` (2): aviso tras stale→Inactive, cierre y sin repetición; sin aviso para Stopped, EndUtc vencido o servicio ya inactivo al abrir.

## Comandos y resultados

- `dotnet build FocusGuard.slnx` (Debug) 0 warnings/0 errores; `dotnet test` Debug **899/899**. En Release, `ColdIdentityTests` falla porque la sonda solo se compila en Debug (preexistente, sin relación). Una prueba de ProcessAgent falló una vez en Release y pasó en la corrida Debug completa; queda anotada como posible prueba inestable.
- React `npm test` **147/147**; `npm run build` (tsc, vite, verify-dist) OK. Extensión 19/19.
- `scripts/build-installer.ps1 -Version 0.1.136` exit 0; `scripts/test-installer.ps1` pasó; `test-browser-extension-package.ps1 -Version 0.1.136` pasó; `FocusGuard-0.1.136-Setup.exe --package-self-test` exit 0.
- Setup 442,890,424 bytes, SHA256 `41A1C27733D9884D9FB0EA78213EB2769C4F3EC56DDEDE29296DB99FF0AD7A63`; zip `EFB198C0…3DAD6`. El payload contiene `NormalSessionShutdownRecorder` y `interruption-notice` en `ui/host.js`.

## UX

El usuario ya no pierde la sesión en silencio: el aviso explica la causa y lleva a Actividad, donde la sesión aparece como «Sesión interrumpida». No se cierra solo, para que no pase inadvertido.

## Seguridad

No cambia la dificultad de desactivación: un administrador puede seguir terminando una sesión normal. Lo que cambia es la trazabilidad, con historial y audit en ProgramData. El audit no incluye datos sensibles (destino fijo «FocusGuard service»).

## Limitaciones

- Un crash o la terminación forzada del proceso no ejecuta `StopAsync`, así que esa pérdida sigue sin registro.
- Si el Host estaba cerrado durante el reinicio, no hay aviso, aunque Actividad sí muestra la sesión interrumpida.
- La liberación de política depende de que el Agent siga conectado durante la parada; si no, el Agent la pierde igualmente al desconectarse.
- **.136 no está instalado:** falta la aceptación instalada (FG-406).

## Siguiente tarea

FG-406: con autorización, instalar .136 y repetir FG-404 comprobando el historial, el audit, el bloqueo liberado y el aviso del Host.
