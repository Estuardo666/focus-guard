# FG-352 — Pulido de copy/diálogos (.121) y bloqueo web real en Chrome

**Fecha:** 2026-10-09 local  
**Fase:** 7, 8 y 9  
**Estado:** .121 instalado con autorización explícita del propietario y aceptado en el Host real; bloqueo web real en Chrome verificado.

## Cambios (todos con pruebas; React 131/131, build y contratos pasan)

- `.session-end-dialog{margin:auto}`: el diálogo de fin de sesión estaba descentrado por el reset de Tailwind (como el diálogo estricto). Medido en el Host instalado: `left` 202,5 con 1200 px de ancho y diálogo de 780 px. Se amplió el contrato de `verify-dist.mjs`.
- Programación y Preferencias repetían título y descripción de la página; ahora el bloque queda solo para lectores de pantalla y quedan visibles las acciones. El botón local «Actualizar estado» pasa a «Actualizar horarios» / «Actualizar preferencias» (había dos botones con el mismo nombre y distinto efecto).
- Resumen de sesión: el UUID completo pasa a 8 caracteres con el completo en `title`, y el fragmento «perfil guardado actual» pasa a una frase: «Se usa el perfil guardado actual, no una copia de la sesión original.» (EN equivalente).
- El centrado de `.session-end-dialog` quedó resuelto aquí; se retiró la tarea separada que se había propuesto.

## Instalación

Preflight (`InstalledVersion 0.1.120`, `Expired`, Strict off) → `install-focusguard.ps1 -ClientMetadataQueryConsent` desde un proceso ya elevado. Verificación: extensión 0.1.121; 7/7 binarios y 18/18 recursos UI idénticos al paquete; servicio `LocalService` Running; `Inactive`. Paquetes Chrome/Edge e instalador pasan; manifest 31/31; UI del `dist` idéntica al payload. Setup `artifacts/installer/FocusGuard-0.1.121-Setup.exe`, 442857656 bytes, SHA-256 `1A279A122BBC69B6AF83D849B48AF37EBFEAC37103A36F77D1266608A4976827`; ZIP SHA-256 `B1C25BEB8A8B9ACD6E76A04A42FA8337CFCFD321A3ABE2B5715BE58C05120B4C`; payload SHA-256 `E301FA57C0C626A1DF9646C6327C4C717F89A431D4DECE8BFA2CC86BD3531CF0`. Log: `artifacts/fg352-install-0.1.121/install.log`.

## Bloqueo web real (Chrome, extensión instalada)

Perfil `2`, 2 min, bloqueo activado. Servicio `Active`, `enforcementConnected=true`, «Confirmación de bloqueo: Confirmada». En una pestaña propia del grupo MCP se navegó a `https://www.facebook.com/`: la pestaña terminó en `chrome-extension://emhdinkjpinjnmldipnejnbeefgonjnj/status.html` con el título **«Page blocked by FocusGuard»**. El audit registró `Blocked https://www.facebook.com/` y el resumen React muestra **«Intentos bloqueados: 1»**; la sesión expiró sola con `extensionReady=true`. La pestaña de prueba se cerró y las pestañas habituales del usuario no se tocaron.

## Observaciones

- La validez de la revisión normal se limita al fin de la sesión (`min(expira, fin)`, `DashboardCommands.cs`), por eso «Revisión válida hasta» coincide con «Fin exacto» en sesiones cortas; es intencional.
- Con .120+ la lectura inicial de Preferencias ya funciona, así que el Host aplica el tema guardado por el usuario (Claro). Antes arrancaba siempre oscuro porque esa lectura fallaba.
- Pruebas que cambian preferencias compartidas (fin de sesión «Ventana», duración) se restauraron a los valores previos y se verificaron en `desktop-settings.json`.

## Pendiente

Contrato de horarios de 1 min (decisión del propietario), extensión Edge .121, strict real y reboot/recovery, DPI/monitor real, Windows 11, Narrator/forced-colors, notificaciones/bandeja con UI cerrada, carga sostenida y Fase 10 (Avalonia se mantiene).
