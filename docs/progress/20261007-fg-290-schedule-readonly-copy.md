# FG-290 — Motivo preciso de sólo lectura en Programación

Estado actual: paquete .100 completado exit0 y validado31/31 manifest, UI18/18, Chrome/Edge hashes correctos. Recibo D:/TEMP/focusguard-fg166-validation/artifacts/fg290/build-receipt-0.1.100.json. Setup SHA256 BB0A6349F1A42C29F76DCA12310B572ABAC84BD7FE996E9BF7BAD731A4AAA353. .100 supersede .99 para aceptación acumulada; instalado todavía .98.

Plan: distinguir autorización de piloto frente a incompatibilidad del Host. Reutilizar el aviso ES/EN editorPilotRequired y conservar todas las restricciones de edición. Validar modo consulta y Host piloto incompatible sin enviar comandos de escritura.

Hallazgo instalado .98: Programación decía Actualiza el piloto para editarlos incluso cuando el ejecutable moderno se abrió sin --pilot. Ahora, con catálogo conocido y pilotEnabled=false, explica el modo de sólo lectura y cómo abrir el piloto. Con piloto habilitado y schedules.editingEnabled=false mantiene el aviso de compatibilidad. Mientras los datos cargan no inventa un diagnóstico de versión.

Regresiones: ScheduleEditor3/3, ambas razones diferenciadas; Nueva programación deshabilitada y sin schedule.save. Suite React114/114; Host154/154, Desktop189/189, Client111/111 y scripts/test-installer pasan. Paquete acumulado .100 completo: manifest31/31, UI18/18, Chrome/Edge14 entradas con hashes correctos y cuatro smokes Host exit0. Recibo D:/TEMP/focusguard-fg166-validation/artifacts/fg290/build-receipt-0.1.100.json. Aún instalado .98; aceptación visual y Repeat pendiente FG-291.

La primera suite bajo carga informó113/114: el strict dialog lazy no había renderizado todavía. La prueba ahora espera el diálogo bajo act/vi.waitFor5s sin quitar assertions.

Resuelto el fallo de prueba: DashboardStart usa React.lazy para StrictReviewPanel; vi.waitFor comprueba el diálogo bajo act con timeout5s y mantiene assertions de consentimiento/recovery. Suite completa114/114 pasa bajo build paralelo.

Observación de build: Setup está publicado en artifacts/setup-publish/0.1.100; todavía no copiado al paquete. El script después de publish ejecuta verificaciones --package-file-hash antes de Copy-Item y ZIP final. Proceso PowerShell15756 y sesión74943 permanecen activos; no reiniciar ni afirmar que el paquete final existe sólo por existir el Setup en publish. Manifest provisional31 entradas, una pendiente (Setup); UI18/18 coinciden.
