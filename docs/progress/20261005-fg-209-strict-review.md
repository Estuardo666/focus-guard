# FG-209 — Review strict y recuperación en Host/React

## Alcance completado

La pantalla React puede preparar e iniciar timers y ciclos finitos en modo strict mediante el protocolo de preparación autoritativo del servicio. El servicio conserva la autoridad sobre la duración, las fases, el estado listo y la expiración de la preparación. La demora de recuperación es de 15 minutos.

El Host crea un código de recuperación y lo mantiene temporalmente en memoria dentro de una review vinculada al documento WebView y al ID de preparación. La interfaz recibe el código sólo para mostrarlo durante esa review. Antes de permitir activar, exige que el usuario guarde el código, reconozca que la sesión puede afectar trabajo sin guardar y escriba exactamente el nombre del perfil. El Host vuelve a comprobar el documento, la review y el estado del servicio, consume la operación antes de enviar la activación y no reintenta si la respuesta deja el resultado incierto.

Cancelar, reemplazar/navegar el documento, preparar otra review o expirar invalida el estado temporal; el servicio expira la preparación por su cuenta. Los controles normales de detener y saltar una pausa siguen prohibidos para sesiones strict. El helper de recuperación continúa siendo una utilidad separada que requiere elevación: React muestra la ruta instalada y los pasos manuales, sin abrirlo ni iniciar UAC.

El guardado explícito del código usa un diálogo nativo de Save File. La interfaz conserva la opción de selección/copia manual y no registra el código en el journal. Las reglas de descanso se reusan sin transformación; si la política guardada no es válida, se rechaza la preparación.

## Cambios principales

- Client: preparación strict para timers desde un snapshot inmutable del perfil y validación reutilizable de ciclos.
- Host: comandos tipados `session.strict.prepare`, `session.strict.activate`, `session.strict.cancel`, `session.strict.saveRecoveryCode` y `recovery.info`; una sola operación pendiente por documento; guardado nativo bajo acción explícita del usuario.
- React: diálogo strict de dos pasos, revisión de perfil/reglas/fin/fases, confirmaciones bloqueantes, guardado del código y sección de ayuda con ubicación real del helper.
- Distribución: los diez chunks locales están alineados entre el build, el allowlist WebView2, el payload Setup y el empaquetador.

## Validación

- `npm test`: 12 archivos y 66 pruebas aprobadas.
- `npm run build`: TypeScript, Vite, CSP, fuentes y lista de chunks locales aprobados. Durante la verificación se detectó que `verify-dist.mjs` enumeraba cuatro chunks antiguos; se actualizó a los diez chunks que realmente carga el producto y el build pasó.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 129 pruebas aprobadas, incluidas las pruebas de sesión strict sincronizadas al checkout principal.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: compilación correcta, cero advertencias y cero errores.
- `graphify update .`: 9,844 nodos, 24,898 aristas y 981 comunidades. La visualización HTML se omite porque el grafo supera el límite; el JSON y el informe se actualizaron.
- El builder de Release creó el paquete 0.1.58. Los 29 archivos declarados en `manifest.json` existen; el ZIP menú contiene 1,730 entradas y se verificaron los chunks strict/recovery. La validación del builder comparó los archivos Host/UI incrustados en Setup con los publicados. El SHA-256 del Setup autónomo coincide con el Setup dentro del paquete.

Setup autónomo (`D:\TEMP\focusguard-fg166-validation\artifacts\fg209\FocusGuard-0.1.58-Setup.exe`): SHA-256 `B03507533D9FEDD992ADAD007F7E7391B18A4E94C7D3371BEAD7E91BCFFABB6C`.

ZIP menú (`D:\TEMP\focusguard-fg166-validation\artifacts\fg209\FocusGuard-0.1.58-win-x64-menu.zip`): SHA-256 `0FC5C044C6E66BDFE147FB2D15C0FE40484447BBFE9592589D903C40C2B4BFF8`.

## Pendiente

El paquete no se instaló y no se modificaron el servicio, la sesión del usuario ni sus reglas. La aceptación instalada de LocalService y un ejercicio real de recovery quedan como gate separado. También quedan abiertos el replay strict mediante una review nueva, la decisión sobre `Block` en reglas de descanso y el resto de paridad/aceptación de Fases 4–10. FG209 completa esta implementación y paquete, no la migración total.
