# FG-298 — Jerarquía de solo lectura en Nueva sesión

Estado: plan previo a implementación.

## Problema comprobado
En el Host React .100 instalado, la cabecera declara “Solo lectura” y bloquea Revisar sesión. El código también deshabilita los controles mediante allowed; no se pueden iniciar ni preparar sesiones desde esa ventana. Sin embargo, la opción de temporizador seleccionada conserva el borde/acento mint y los campos deshabilitados no tienen un estilo visual propio. El aviso de requisito se muestra como texto pequeño alineado a la derecha. La captura instalada confirma que esto hace parecer interactivo el formulario.

## Cambio propuesto
1. Mantener semántica nativa disabled, la condición allowed, y la asociación aria-describedby.
2. Dar a controles de sesión deshabilitados un tratamiento visual de vista previa con tokens semánticos existentes; neutralizar el acento de la opción seleccionada cuando su fieldset está deshabilitado.
3. Presentar el motivo junto a Revisar sesión como un aviso legible, alineado a la izquierda, y actualizar el texto ES/EN para nombrar solo lectura y la siguiente opción real.
4. Añadir regresión que comprueba el bloqueo real de controles, el aviso y la asociación accesible.
5. Alinear manifest de la extensión y paquete React a .101; ejecutar pruebas/build de React, extensión y paquete, revisar dist y regenerar Setup versionado. Mantener instalación .100 hasta aceptación owner-present de .101.

## Implicaciones
UX: se distingue una vista previa inerte del formulario editable sin eliminar información de contexto. UI/UX Pro Max aporta jerarquía/contraste y accesibilidad; Emil respuesta clara sin animación; Taste/Frontend Design respetan la identidad específica de FocusGuard y evitan una empty-state genérica; Apple Design prioriza propósito, agencia y estado comprensible.
Seguridad: no se toca autorización, IPC ni bloqueo; allowed y disabled se conservan.
Recuperación: no se cambia el contrato del Host; rollback del instalador sigue siendo necesario tras generar el paquete.

## Aceptación
Regresión ES/EN; TypeScript, tests y producción React pasan; todos los inputs continúan disabled en read-only; dist conserva CSP/font local y sus verificadores; recibo de Setup .101 y hashes generados. Sin instalación ni aceptación visual del paquete nuevo, no se marca instalada.

