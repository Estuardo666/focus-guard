# FG-213 — Objetivo opcional por sesión React

## Alcance completado

El formulario permite ingresar un objetivo local opcional de hasta 240 caracteres. El campo tiene etiqueta visible, contador y una nota que informa dónde se guarda. Normal timer, ciclo y strict muestran el objetivo en la review antes de confirmar; strict lo presenta en el segundo paso junto a las consecuencias de locking. Repeat restaura el texto como borrador editable.

El cliente sólo incluye el objetivo en la confirmación final. El parser lo exige en timer/ciclo/strict y rechaza propiedades adicionales o texto que exceda el límite. El Host añade el objetivo a `SessionIntention` sólo después de que el servicio acepta y devuelve un sessionId, mode y horario que coinciden con la preparación. El servicio de bloqueo, sus contratos IPC, extensiones y logs no reciben el objetivo. Si el resultado de activación es desconocido, el Host no escribe intención aceptada.

## Aplicación de skills

- UI/UX Pro Max: campo etiquetado, controlado, con foco visible y límites/helper persistentes.
- Emil: interacción de escritura frecuente sin animación; confirmación inmediata y feedback de estado existente.
- Taste: disciplina de brief y anti-default; no se importan patrones de landing porque el ámbito es un formulario dashboard/multi-step, fuera del alcance declarado por la skill.
- Front-end Design: se conservan identidad gráfica graphite/mint, Geist y superficie existente.
- Apple Design: interacción inmediata de teclado/puntero y sin gestos/movimiento que retrasen el trabajo.

## Verificación

- `npm test`: 16 archivos, 73 pruebas aprobadas. Incluye el valor restaurado en Repeat, revisión normal/strict, confirmación y privacidad local.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 139 pruebas aprobadas. Timer, ciclo y strict guardan objetivo después de la aceptación; parser admite exactamente 240 caracteres y rechaza 241 o un campo ausente.
- `dotnet test tests/FocusGuard.Client.Tests/FocusGuard.Client.Tests.csproj -c Release --no-restore`: 109 aprobadas.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings y 0 errors; el target ejecuta TypeScript, Vite, CSP y allowlist de 10 chunks. Bundle principal 487.54 kB.
- `graphify update .`: 9,944 nodos, 25,249 aristas, 987 comunidades; JSON/reporte actualizados y HTML omitido por límite.
- El Setup 0.1.62 incluye 29/29 recursos del manifiesto; ZIP con 1,730 entradas. Los tres archivos Setup standalone/paquete/publicación tienen hashes idénticos y los recursos Host/UI embebidos coinciden con sus archivos publicados.

Setup autónomo (`D:\TEMP\focusguard-fg166-validation\artifacts\fg213\FocusGuard-0.1.62-Setup.exe`): SHA-256 `B0426FAF75879E304B806BCC491AA9F360BD733D169CA197B6C85F2C6DAF0213`.

ZIP menú (`D:\TEMP\focusguard-fg166-validation\artifacts\fg213\FocusGuard-0.1.62-win-x64-menu.zip`): SHA-256 `5098A54691979C5F979F49B26555DE4010BEC6F15CA97293BF076CA0DCE3E5B7`.

## Pendiente

El paquete se generó y verificó; no se instaló. La revisión visual con screenshot no pudo ejecutarse en esta máquina: Orca no está disponible y el browser headless no produjo captura. La prueba DOM y compilación sí cubren el campo. Las reglas `Block` en descansos y la aceptación instalada/Fases 4–10 siguen abiertas.
