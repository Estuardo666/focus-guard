# FG-220 — Ayuda React alineada con las funciones actuales

## Resultado

La pestaña Ayuda ya no afirma que los ciclos, el modo estricto y los horarios sólo estén disponibles en Classic. Indica qué puede gestionar React durante el piloto, conserva las instrucciones de conexión/cierre y añade qué reglas se pausan y cuáles permanecen bloqueadas durante descansos. El panel de acceso a recuperación strict sigue enlazado desde Ayuda.

El copy usa términos del producto y describe el comportamiento visible sin prometer continuidad de bloqueo no comprobada. No se añadieron animaciones ni decoración a la guía.

## Verificación

- React: 17 archivos, 83 pruebas aprobadas. El test de App abre la pestaña Ayuda y verifica el texto actualizado.
- Host: 141/141; Client: 109/109; Storage: 38/38; Desktop: tres pruebas focalizadas de ciclo/descanso.
- Release solution build: 0 warnings, 0 errores; TypeScript, Vite, CSP y chunks locales aprobados.
- Graphify: 10,030 nodos, 25,551 aristas, 1,000 comunidades.
- Package 0.1.69: 30/30 recursos del manifiesto, 0 ausentes, 1.731 entradas ZIP. Setup SHA-256 `97A7278B3CA0D394BA7A0DCB943CEF7F9EA6D97C995256A522B1B60A02C82DB7`; menú ZIP `C2A69799E0FB0E1C4A3194B419E5A762DE4D4F0C251CEC8038439622C133351E`. No instalado.

## Pendiente

La verificación visual del Host con 0.1.69 y DPI real sigue pendiente. La ventana instalada que se abrió/cerró pertenece al paquete anterior y no valida el nuevo copy; su [captura](/D:/TEMP/focusguard-fg166-validation/artifacts/fg220/installed-host-before-update.png) conserva la evidencia visual del conteo defectuoso. El servicio siguió `Ready`, la extensión `Ready` y la sesión `Inactive`. La instalación actualizada requiere un proceso elevado; el PowerShell de esta sesión no tiene ese token.
