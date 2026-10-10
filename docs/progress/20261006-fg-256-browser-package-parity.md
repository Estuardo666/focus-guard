# FG-256 — Integridad de paquetes Chrome y Edge durante la instalación

## Plan y hallazgo

La instalación .86 conservaba los archivos de distribución .85 aunque el resto de los ejecutables coincidía con .86. El instalador copiaba `browser-extension\*`, pero su verificación posterior no cotejaba `distribution.json` ni los ZIP versionados. Así podía declarar éxito con artefactos de navegador viejos. La diferencia no demuestra que el instalador causara el 7034 del servicio; son problemas separados.

## Cambio

- Antes de la transacción, el instalador comprueba que la versión de `browser-extension\distribution.json` coincide exactamente con `manifest.json` del paquete.
- Exige dos entradas únicas, Chrome y Edge, nombre `FocusGuard-<version>-<browser>.zip`, SHA-256 hexadecimal válido y archivo dentro del directorio del paquete. También calcula el hash real de cada ZIP antes de permitir la instalación.
- Añade ambos ZIP y `distribution.json` a la verificación posterior a la copia; cualquier ausencia o hash distinto falla la transacción y activa su ruta de rollback existente.
- La prueba funcional extrae sólo las funciones relevantes del AST del instalador y crea un paquete/instalación bajo TEMP: la copia íntegra pasa y una mutación del ZIP Edge instalado se rechaza.
- El primer barrido detectó tres condiciones PowerShell con `-or` en la línea siguiente, que el parser no aceptaba; también faltaba crear `publish\FocusGuard.Host.Windows\ui` en el fixture. Se corrigieron ambos defectos, y la prueba ahora llega a verificar la copia.
- Se subieron la versión de la extensión y del paquete UI a .88 para generar un artefacto versionado coherente.

## Verificación

- `scripts/test-installer.ps1`: pasa, incluye el caso temporal válido y el rechazo del ZIP Edge obsoleto.
- Solución Release: 874/874; React: 104/104; extensión: 19/19.
- Producción React .88, TypeScript, CSP, assets locales y contraste: pasan durante el build.
- Paquetes Chrome/Edge .88: hash cotejado contra `distribution.json`; 14 entradas cada ZIP.
- Setup standalone `--package-self-test`: exit 0; el builder comprobó los hashes del Desktop y de los recursos UI embebidos.
- Host empaquetado `--smoke`, `--smoke-renderer-failure`, `--smoke-renderer-crash`: exit 0 en los tres casos.
- ZIP menú: versión .88 y los 31 archivos declarados en manifest presentes.
- Graphify actualizado: 10,259 nodos, 27,932 aristas y 983 comunidades. No regeneró `graph.html` porque supera el límite de 5,000 nodos.

## Artefactos y límites

Setup y ZIP quedaron en `D:\TEMP\focusguard-fg166-validation\artifacts\fg256`; recibo: `build-receipt.json`. El artefacto .88 no está instalado. Service/Desktop/Host continúan en .86 según la última revisión registrada; esta prueba no inició, detuvo ni modificó una sesión.

La comprobación detecta desajustes accidentales entre paquete y copia. No autentica al editor ni sustituye las firmas de Chrome Web Store o Microsoft Edge Add-ons. La distribución .88 sigue siendo staging; no se probó tráfico real de Edge.

La salida de logs compartida para FG-235 termina a las 12:24:21Z con `RequestTimeout` y `OperationCanceledException` de solicitudes individuales. Son unas 11 horas antes del 7034 de las 18:28 hora local (23:28Z); el código captura esos timeouts por conexión y el listener sigue. No explican la terminación del proceso. No hay evidencia nueva que permita atribuir la causa histórica.

## Siguiente paso

FG-242: volver a consultar el estado de la sesión; cuando sea seguro y con autorización del owner, instalar .88 y completar aceptación visual/teclado. Mantener FG-235 abierto hasta disponer de diagnóstico o transcript correlacionado con otro evento 7034.
