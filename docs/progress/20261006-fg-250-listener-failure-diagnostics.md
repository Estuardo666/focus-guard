# FG-250 — Registrar fallos en los bucles de listeners IPC

## Hallazgo e implementación
Las entradas compartidas RequestTimeout son cancelaciones de solicitudes capturadas por cada conexión; no explican los 7034 históricos. El código tenía otra brecha: una excepción durante la creación/aceptación del pipe podía salir del bucle hospedado sin escribir el diagnóstico persistente.

FocusControlPipeService y ExtensionHealthPipeService ahora reportan la excepción en el límite de su bucle superior y luego la relanzan, conservando el comportamiento original de parada del host. El registro incluye UTC, nombre del worker, tipo, HResult y hasta16 nombres de métodos; omite el mensaje de excepción, rutas y contenido del cliente. Si escribir el diagnóstico falla, esa falla no reemplaza la excepción original.

## Verificación
- Pruebas focalizadas: Service.Tests106/106; incluye fallos inyectados de CreatePipe en ambos listeners, composición DI predeterminada y escritura JSON en carpeta temporal; se verifica que no persiste el mensaje privado.
- Solución completa Release: 874/874, exit0.
- React104/104, build/contraste/CSP, extensión19/19, test estático de instalador pasan.
- Host empaquetado .86: smokes local, fallo de renderer y crash/recovery todos exit0.
- ZIP .86 abre con manifest .86 y1,732 entradas. El Service DLL empaquetado coincide por SHA-256 con el ensamblado publicado; los hashes de Setup, ZIP, UI y extensiones están en D:\TEMP\focusguard-fg166-validation\artifacts\fg250\build-receipt.json.
- Graphify:10,248 nodos,27,868 aristas,993 comunidades.

## Límites
No se instaló .86. El Host instalado sigue en .79 y el token de Codex no está elevado; Setup self-test y smoke gráfico no se ejecutaron. El servicio histórico terminó sin diagnóstico nuevo correlacionado, así que FG-235 sigue abierto: esta instrumentación captura una próxima excepción de listener, no prueba la causa pasada. El PID32316 del Host existente se dejó intacto.