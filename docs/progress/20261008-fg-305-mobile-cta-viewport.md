# FG-305 — CTA visible en el viewport mínimo del Host

Fecha: 2026-10-08
Estado: Setup .102 instalado y aceptación del Host normal completada en viewport mínimo 720×480. FG-305 cerrada; forced-colors y DPI permanecen como validación general de Fase9.

## Hallazgo y cambio
La captura instalada .100 a720×480 mostraba el selector móvil, pero la barra lateral apilada y el chrome superior dejaban Revisar sesión fuera de la primera vista. El contrato de FocusGuard pide que el único CTA de revisión esté visible antes del scroll.

El CSS .102 compacta marca/estado/select en una fila hasta720px, envuelve sólo por debajo de520px, reduce espacios en viewports de baja altura y conserva el CTA junto al título cuando el ancho supera600px. En tamaños menores, el encabezado vuelve a apilarse. No se cambió el mínimo nativo720×480 ni la lógica de sesión.

## Vista visual
La captura anterior instalada es artifacts/fg298-output/fg305-responsive-minimum-host100.png. La comparación posterior artifacts/fg305-output/fg305-preview-readonly-720x480.png muestra el CSS de producción .102 con un fixture visual estático a704×441px, equivalente al área cliente del Host mínimo: selector en una fila, vista previa read-only y Revisar sesión visible con su motivo asociado. Es una revisión de CSS/layout, no una aceptación de React/WebView/bridge instalado. No se conserva una captura del fixture piloto. Tras instalar .102, el Host real de solo lectura se redimensionó a720×480 y se capturó con PrintWindow. UIA confirma Revisar sesión visible y el motivo del estado de solo lectura visible en esa primera vista. Captura: artifacts/fg305-output/fg305-installed-0.1.102-720x480.png.

## Verificación
- React npm test: 114/114.
- React .102 npm run build: TypeScript, Vite, contraste dark/light, CSP/assets locales y contrato responsive del CSS pasan.
- Extension npm test: 19/19.
- scripts/test-browser-extension-package.ps1 -Version 0.1.102: pasan distribución, identidad y hashes Chrome/Edge.
- scripts/test-installer.ps1: pasa.
- Setup .102: 31/31 elementos del manifest presentes; ZIP menu1732 entradas y cubre el manifest; 18/18 archivos UI coinciden por SHA-256 con dist.
- Cuatro Host .102 smokes exit0: local, runtime missing, renderer failure y renderer crash. El servicio instalado conservó PID5704, Ready/Inactive, SessionId nulo.
- Setup independiente .102: 442.849.464 bytes, SHA-256 EFD8075B08BC7BA25FDBF0D07F80E7324EF48D7AC40658A10E15BE5AB1EED52F.
- ZIP menú: 699.168.976 bytes, SHA-256 65E6C58CB03802090049D60C89B4C246085D2308BF0FBB683741ECA1B16FDFBA.
- Receipt: artifacts/fg305-output/build-receipt-0.1.102.json; smokes: artifacts/fg305-output/fg305-host-smokes-0.1.102.json.

## Riesgos, seguridad y límites
El cambio sólo afecta layout. Se conserva la semántica de disabled/allowed, permisos, IPC y autoridad LocalService. La observación al crear este checkpoint corresponde a .100 y a Setup .101 abierto. La primera revalidación posterior confirmó .101 instalado, servicio PID12052 Running bajo LocalService y ningún proceso Setup/FocusGuard.UI abierto en ese instante. El token actual de Codex no está elevado. Setup .102 aún no se instaló; después de esa consulta quedó abierto como proceso elevado PID21716, listo para instalar. La aceptación visual real del Host .102, forced-colors, DPI y Windows 10/11 siguen pendientes.

## Prechequeo antes de instalar .102 (10:09 local)

El manifiesto instalado declara versión 0.1.101. El proceso de servicio permanece Running bajo LocalService (PID12052). No se observó un proceso Setup o FocusGuard.UI.exe durante la consulta. La consola de Codex informa IsInRole(Administrator)=false. Setup .102 se lanzó desde el artefacto validado y ahora está abierto con token elevado (PID21716); su UI muestra Listo para instalar y el botón Instalar habilitado, pero el control no expone InvokePattern a esta sesión. No se automatizó UAC ni se pulsó Instalar. El artefacto permanece en artifacts/fg305-output/FocusGuard-0.1.102-Setup.exe con SHA-256 EFD8075B08BC7BA25FDBF0D07F80E7324EF48D7AC40658A10E15BE5AB1EED52F. Luego, el Host normal .101 informó Estado actualizado, Servicio Conectado, Extensión Disponible y Sin sesión activa; el dato de estado corresponde a su consulta a las 9:50 local, antes de cerrar el Host. No se inició ninguna sesión. Evidencia UIA: artifacts/fg305-output/fg305-preinstall-status-0.1.101.json.

## Elevación y consentimiento antes de instalar

El shell actual de Codex y sus procesos padre tienen token medium. La política UAC local informa EnableLUA=1, ConsentPromptBehaviorAdmin=0 y PromptOnSecureDesktop=0; Windows eleva automáticamente procesos administrativos nuevos para esta cuenta. No se modificó la política. Setup .102 (PID21716) ya está elevado y listo. La política IPC instalada .101 contiene clientMetadataQueryConsent=true, pero Setup vuelve a pedir consentimiento antes de cada instalación y el script exige explícitamente ese switch. Luego, el usuario respondió “Sí autorizo”. El Setup .102 completó la instalación; el estado final se documenta abajo.

## Aceptación instalada .102

Setup muestra “FocusGuard está listo” y el manifiesto bajo Program Files declara 0.1.102. El servicio FocusGuard está Running como NT AUTHORITY\LocalService, PID25096. El manifiesto IPC mantiene clientMetadataQueryConsent=true tras la autorización del usuario.

El Host 0.1.102 normal, sin --pilot, abrió con SOLO LECTURA y sin sesión activa. El viewport se ajustó al tamaño mínimo exterior 720×480 (área cliente 704×441), se capturó en artifacts/fg305-output/fg305-installed-0.1.102-720x480.png y luego se restauró a1216×799. UIA observó Revisar sesión enabled=false, IsOffscreen=false; el texto “Para iniciar una sesión, abre FocusGuard en modo piloto o usa FocusGuard clásico.” también quedó dentro del viewport. Esto verifica visibilidad y copy, no habilitación de sesiones desde el Host normal.

Después de invocar Actualizar estado en la UI instalada a las 10:10 local, UIA informó Servicio Conectado, Extensión Disponible y Sin sesión activa; el servicio continuó con PID25096. No se inició ninguna sesión. SHA-256 de los 18/18 recursos UI instalados coincide con src/FocusGuard.UI/dist. Evidencia de prechequeo y Setup: artifacts/fg305-output/fg305-preinstall-status-0.1.101.json y artifacts/fg305-output/fg305-setup-ready-0.1.102.json.
