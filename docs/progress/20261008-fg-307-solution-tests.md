# FG-307 — Barrido completo de pruebas .NET tras FG-305

Fecha: 2026-10-08
Estado: aceptado.

## Verificación
Comando: dotnet test FocusGuard.slnx -c Release --no-restore
Resultado: exit0; los14 ensamblados de pruebas reportaron Passed y cero fallos:
Domain4, Application39, Monitoring12, Contracts62, Storage40, Infrastructure.Windows15, NativeMessaging23, ProcessAgent26, Ipc.Windows81, Service106, Client111, Host.Windows154, Desktop189 y Desktop.UiTests15; total877.

Durante el barrido, Host.Windows volvió a ejecutar el build frontend focusguard-ui@0.1.102: TypeScript/Vite, contraste dark/light, verificador de layout mínimo 720×480, CSP y assets locales pasaron. El build/paquete .102 conserva su recibo y los cuatro smokes Host .102 pasan según FG-305.

No se cambió código durante este barrido. La instalación .100 sigue Ready/Inactive; falta aceptación owner-present de .102.
