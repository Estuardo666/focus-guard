# FG-210 — Replay strict con una review nueva

## Alcance

El Host permite repetir sesiones strict aceptadas desde el historial como configuración editable. La intención se ofrece sólo si el perfil actual sigue existiendo, el consentimiento de enforcement está guardado explícitamente y la configuración de ciclo pasa validación. Las entradas antiguas con consentimiento desconocido y los perfiles eliminados siguen sin poder repetirse.

React repone timer/ciclo, perfil actual, duración, consentimiento y configuración. El modo strict queda preseleccionado y se advierte que se generará otro código de recuperación y que se exige una review strict completa. Al continuar, la UI llama a `session.strict.prepare`. El servicio/Host vuelve a evaluar el perfil y sus reglas actuales, genera una preparación y código nuevos y requiere las confirmaciones de FG209. Repetir nunca reusa el código anterior ni inicia la sesión automáticamente. El journal sigue guardando metadatos de configuración, no credenciales.

La decisión sobre reglas `Block` de descanso no cambia. Las reglas incompatibles siguen rechazándose, sin conversión automática.

## Validación

- `npm test`: 13 archivos, 69 pruebas aprobadas.
- `npm run build`: TypeScript, Vite, CSP, fuentes y diez chunks locales aprobados. El bundle principal quedó en 484.85 kB.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 133 pruebas aprobadas.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 advertencias y 0 errores.
- `graphify update .`: 9,880 nodos, 25,016 aristas, 971 comunidades; JSON/informe actualizados. El HTML se omite por el límite de 5,000 nodos.
- Paquete 0.1.59: 29/29 archivos del manifest, ZIP menú de 1,730 entradas y recursos strict/replay comprobados. El builder validó los hashes de HTML/CSS/JS/chunks incrustados; las copias del Setup tienen el mismo SHA-256.

Setup autónomo (`D:\TEMP\focusguard-fg166-validation\artifacts\fg210\FocusGuard-0.1.59-Setup.exe`): SHA-256 `D2A0EFE68E150E4B8AFCC5A145AECBF3100464B45E4651D875B1B48513EC9FEC`.

ZIP menú (`D:\TEMP\focusguard-fg166-validation\artifacts\fg210\FocusGuard-0.1.59-win-x64-menu.zip`): SHA-256 `B57853C912471AAD5B98B8E37B0D1D22A80B8E9B47654902D51F47042E1DEB12`.

## Pendiente

El paquete se generó, pero no se instaló. Siguen pendientes la semántica de `Block` en descansos, la aceptación instalada de LocalService/recovery real y el resto de paridad de Fase 8/Fases 4–10. FG210 no declara terminada la migración general.
