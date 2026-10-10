# FG-267 — Plan de continuidad .91 desde React

## Objetivo

Cubrir la parte de inicio y continuidad de Fase7 en el Host instalado .91 con un timer normal breve. El usuario pidió migrar y validar el flujo de inicio. La instalación actual reporta servicio `Ready` y sesión `Inactive`.

## Secuencia

1. Leer `DrillClient status` y UIA de la vista Resumen.
2. Comprobar explícitamente `Temporizador` seleccionado, Strict apagado y «Activar bloqueo durante la sesión» desmarcado. Cambiar sólo la duración de este formulario a1min.
3. Abrir la revisión, verificar duración finita/normal, perfil seleccionado, Strict=false y EnforcementEnabled=false; sólo entonces confirmar inicio.
4. Leer Service status/SessionId/remaining desde DrillClient; cerrar únicamente la ventana Host y reabrir Program Files `FocusGuard.UI.exe --pilot` como el mismo usuario. Confirmar que el Service mantiene la misma SessionId y que el countdown avanza.
5. No enviar Stop. Esperar el límite de un minuto y comprobar estado terminal/expiración automática, reconexión del Host y SessionId/historial reportados.

## Límites

Esta pasada prueba inicio, representación y continuidad de un timer sin enforcement ni strict. No verifica bloqueo de aplicaciones o Edge, ni reemplaza el caso independiente de continuidad bajo enforcement activo. Si alguna precondición de seguridad aparece activada o la revisión describe restricciones, cancelar la preparación sin confirmar.
