# FG-270 — Supervisión del ProcessAgent desde el Host React

## Hallazgo

En la instalación .91, el Host React preparó y confirmó una sesión normal de un minuto con la única regla `Block: FocusGuard.TestTarget.exe`, modo estricto apagado y enforcement activado. El servicio permaneció en `Activating`, `enforcementConnected=false`, y expiró automáticamente. No se lanzó el ejecutable de prueba. La inspección del código muestra que `ProcessAgentSupervisor` sólo se inicia desde `FocusGuard.Desktop`; `FocusGuard.UI.exe` no lo inicia.

## Plan de implementación

1. Añadir un alcance de vida del supervisor en `FocusGuard.Host.Windows`, reutilizando `ProcessAgentSupervisor` y el directorio de instalación protegido del Host.
2. Iniciarlo para ejecuciones normales del Host antes de abrir la UI y no durante los escenarios `--smoke`; cancelarlo y esperar su cierre al salir del Host. No terminar el proceso Agent al cerrar la ventana.
3. Añadir una prueba automatizada de arranque inmediato, cancelación e idempotencia del alcance.
4. Ejecutar las pruebas Host y Desktop relevantes, build de solución y smokes del Host; después generar el instalador versionado y verificar el payload.
5. Repetir el ensayo instalado con el perfil temporal, sólo tras confirmar `Active` y `enforcementConnected=true`; dejar la sesión finita expirar y limpiar el perfil.

## Límites y aceptación

El ensayo fallido no modificó las reglas de perfiles existentes, no lanzó `FocusGuard.TestTarget.exe` y terminó en `Expired`. La lectura de `ProgramData\FocusGuard\diagnostics\service-failures.jsonl` no está disponible para el proceso de Codex por ACL; el diagnóstico propietario indica `Ready`. No se afirmará bloqueo hasta que el servicio confirme la conexión del Agent y la instancia de prueba termine por enforcement, antes del vencimiento propio del proceso.
