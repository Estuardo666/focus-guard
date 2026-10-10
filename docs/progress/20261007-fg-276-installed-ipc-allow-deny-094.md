# FG-276 — Fase 4: aceptación IPC instalada en .94

## Resumen

Verifiqué la identidad y el manifiesto instalados de .94, acepté una consulta Diagnostic legítima y comprobé el rechazo de un ejecutable local fuera de la raíz protegida contra el servicio real bajo LocalService. El servicio se mantuvo estable e Inactive.

## Archivos y artefactos

- Plan actualizado: docs/FOCUSGUARD_UI_MIGRATION_PLAN.md
- Estado de tareas: TASKS.md
- Recibo .94: D:/TEMP/focusguard-fg166-validation/artifacts/fg274/build-receipt.json
- Evidencia de manifest y prueba local: D:/TEMP/focusguard-fg166-validation/artifacts/fg275/installed-ipc-allow-deny-0.1.94.json
- Harness temporal StatusRequest: D:/TEMP/focusguard-fg166-validation/artifacts/fg275/Program.cs y UnauthorizedStatusProbe.csproj

No se modificó código de producto ni se añadieron tests al repositorio.

## Verificaciones y comandos

- El manifiesto C:/Program Files/FocusGuard/ipc-policy.json tiene schema 1; owner SID coincide con el usuario actual, installRoot coincide con Program Files y hashes EXE/DLL de todos los roles coinciden. Root y manifiesto pertenecen a Administrators.
- FocusGuard.Service PID18792 corre como LocalService. DrillClient Diagnostic devolvió serviceReady=true, extensionReady=true y sessionState=Inactive, sin SessionId.
- La sonda temporal compilada en D:/TEMP envió un StatusRequest válido a FocusGuard.Control.v2 desde fuera del install root. Resultado DENIED_EOF. Después, DrillClient siguió devolviendo Ready/Inactive y el servicio conservó PID18792.
- dotnet test tests/FocusGuard.Ipc.Windows.Tests/FocusGuard.Ipc.Windows.Tests.csproj --configuration Release --no-restore --nologo: 81/81.
- El primer intento de invocar el harness usó la ruta equivocada antes del build de salida; el ejecutable no corrió. Corregí la salida explícita del build, verifiqué la existencia y hash del EXE y sólo entonces ejecuté la sonda.

## UX y seguridad

No se envió ningún comando de activación/parada, no se cambió política y no se lanzó TestTarget. El rol Diagnostic está provisionado intencionalmente como Control para ensayos del owner; este checkpoint ejerció únicamente StatusRequest. La matriz de política permite Control a Desktop/WebViewHost/Diagnostic y lo niega a ProcessAgent/NativeMessaging.

## Límites pendientes

DENIED_EOF prueba el rechazo de una imagen local fuera de la raíz; no prueba un rol incorrecto desde un ejecutable permitido por el manifiesto. El bloqueo real de aplicación sigue aceptado en .92, no en .94. No se completó rollback owner-present porque el token de ejecución actual no está elevado. La aceptación visual .94, teclado/foco, forced-colors y aviso Activating en WebView2 también siguen pendientes. extensionReady no verifica Edge.

## Siguiente tarea

Aceptar el editor de Perfiles y el aviso Activating en el Host instalado .94. Después, probar un rol incorrecto desde un ejecutable/ruta autorizados por el manifiesto y completar el rollback owner-present en un contexto ya elevado, sin elevar automáticamente.