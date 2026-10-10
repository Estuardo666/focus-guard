# FG-243 — Host WebView2 .80: carga local y recuperación

## Pruebas ejecutadas

Ejecutable del paquete .80: `D:\TEMP\focusguard-fg166-validation\artifacts\fg241\installer-retry\publish\FocusGuard.Host.Windows\FocusGuard.UI.exe`.

- `FocusGuard.UI.exe --smoke`: carga local WebView2 y bridge fallback; exit 0.
- `FocusGuard.UI.exe --smoke-renderer-failure`: recovery UX después del fallo simulado; exit 0.
- `FocusGuard.UI.exe --smoke-renderer-crash`: invoca `Page.crash` en el renderer aislado y verifica su recuperación; exit 0.

Los modos smoke usan un perfil de datos temporal propio, no llaman al servicio instalado ni envían comandos de sesión. El token permaneció con integridad media. No hubo cambios de servicio ni de perfiles FocusGuard.

## Límite de aceptación

Esto prueba que el Host .80 y sus assets locales cargan y recuperan el renderer, pero no autentica contra LocalService. El Host instalado sigue en .79; falta instalación autenticada de .80 y validación visual con esa versión. Fase5 conserva esas puertas pendientes.
