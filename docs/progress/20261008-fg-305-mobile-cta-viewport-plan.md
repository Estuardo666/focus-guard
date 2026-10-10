# FG-305 — CTA visible en el tamaño mínimo del Host

Estado: plan previo a implementación.

## Hallazgo
La captura del Host .100 a 720×480 DIP confirma que el selector móvil aparece, pero el riel de marca/selector y el espacio superior empujan “Nueva sesión” al borde inferior; Revisar sesión queda fuera del primer viewport. La UX_SPEC pide mantener el CTA de revisión visible antes de desplazarse.

## Cambio
1. En ≤720px, compactar la barra móvil en una fila con marca, estado de modo y selector nativo etiquetado.
2. En alturas ≤560px, reducir sólo el espacio vertical de la cabecera, estado de conexión y panel inicial; el cuerpo conserva scroll vertical.
3. Mantener el CTA junto al encabezado en anchuras donde ambas columnas caben y conservar la versión apilada en ventanas más estrechas.
4. Añadir un contrato de build para confirmar que los breakpoints y selectores responsive están presentes en el CSS publicado.
5. Ejecutar React tests/build/contraste/CSP, regenerar graph y Setup .102 en una salida nueva. No cambiar políticas ni sesión.

## Seguridad, UX y recuperación
UX: la navegación de sección, estado Pilot/solo lectura y acción primaria caben en el viewport mínimo. Los campos y navegación conservan sus nombres accesibles; se evita scroll horizontal.
Seguridad: no cambia IPC, autorización, reglas ni inicio de sesión.
Recuperación: no cambia el tamaño mínimo nativo 720×480; rollback permanece disponible en el instalador.

## Aceptación
Regresión del contrato CSS; React suite, TypeScript, build de producción, contraste y CSP pasan; manifest/UI/extension package .102 coinciden; cuatro smokes locales Host exit0. La aceptación visual instalada sigue pendiente del Setup .102 porque .100 es la versión instalada y .101 aún no se instala. Captura previa: artifacts/fg298-output/fg305-responsive-minimum-host100.png.
