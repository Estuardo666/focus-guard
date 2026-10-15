# Fase 3 — preservar identidad y estado sync durante una actualización

## Resultado

El consentimiento de sincronización automática y el outbox DPAPI viven bajo el perfil del usuario. Se añadió una regresión a `scripts/test-installer.ps1` para impedir que los comandos de limpieza del instalador apunten a `%LOCALAPPDATA%`, `LocalApplicationData` o a rutas `FocusGuard\Sync`, `FocusGuard\Identity` y `FocusGuard\devices`.

El guard primero comprueba que reconoce ejemplos de rutas de sincronización, identidad y dispositivos bajo el perfil, luego inspecciona los comandos `Remove-Item` y `Remove-ItemProperty` del instalador completo, incluidos sus helpers, no solo el bloque principal de reemplazo. El flujo actual limita las eliminaciones a archivos de instalación/máquina y temporales del propio instalador. No se modifica cómo se actualizan los binarios ni se regenera el paquete `.173`.

## Verificación

- `scripts/test-installer.ps1` — PASS; incluye lanzamiento/proceso, paridad de archivos de Chrome/Edge y ocho casos de reintento de salud autenticada.
- PowerShell parser de `scripts/test-installer.ps1` — PASS.
- Sin instalación ni actualización real, sin cambiar cuenta, vault, consentimiento ni sesiones locales.

## Límite

Este guard protege el código de actualización contra una nueva limpieza directa de datos del perfil; no demuestra el comportamiento de una actualización instalada. La aceptación física de Fase 3 aún debe comprobar que consentimiento, sesión y outbox sobreviven al reiniciar FocusGuard en las dos PCs. Ver el [runbook de aceptación](../cloud-phase0/PHASE3-PHYSICAL-ACCEPTANCE.md).
