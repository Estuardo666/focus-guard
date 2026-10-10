# FG-323 — Guardas del cliente Diagnostic en el candidato .109

Fecha: 2026-10-08
Estado: DONE para el gate CLI del paquete; aceptación contra LocalService instalado pendiente.

## Prueba sobre el binario de paquete

Ejecutable probado: `artifacts/fg321-output/publish/FocusGuard.DrillClient/FocusGuard.DrillClient.exe`.

- `activate-strict`: exit1; “Strict sessions must be started through the reviewed FocusGuard UI; Diagnostic cannot activate sessions.”
- `acknowledge`: exit1; “Diagnostic cannot acknowledge Native Messaging health; that channel belongs to the extension host.”
- En `src/FocusGuard.DrillClient/Program.cs`, ambas guardas preceden la construcción de `VerifiedPipeConnectionFactory`; no se abrió ningún pipe ni se llamó al Service.

SHA-256 ejecutable: `99BA83081D4C3A33D9A51CB85DB7E842E6AB0E9D4F5A5E6BB61BC0AAE3FB3B2D`.
SHA-256 DLL: `349DAD7EBBE634712F0AE62A0A7498AAB9873007A5530EED80C4FA527D381DF1`.

El DrillClient instalado .102 no se ejecutó. Estos son gates locales del candidato .109; no sustituyen allow/deny contra el servicio LocalService instalado, el rollback real ni la actualización owner-present.

## Revalidación del candidato .112

El mismo par de comandos se ejecutó sobre `artifacts/fg328-output/publish/FocusGuard.DrillClient/FocusGuard.DrillClient.exe`. `activate-strict` y `acknowledge` volvieron a salir1 con sus guardas explícitas; EXE/DLL conservan los mismos hashes de .109. No se abrió pipe ni se lanzó el binario instalado .102. Recibo: `artifacts/fg323-diagnostic-cli-112.json`.
