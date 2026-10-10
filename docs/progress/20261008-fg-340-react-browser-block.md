# FG-340 — Bloqueo web real desde la sesión React

**Fecha:** 2026-10-08 local / 2026-10-09 UTC  
**Fase:** 7, aceptación instalada  
**Estado:** Chrome .112 bloqueó un sitio de una regla Block; sesión y pestaña de prueba quedaron limpiadas. Edge sigue pendiente.

## Plan de verificación

1. Confirmar servicio LocalService y `ExtensionReady` mediante el cliente diagnóstico instalado.
2. Iniciar en React una sesión normal finita desde el perfil guardado `2`, con bloqueo activado y modo estricto apagado.
3. Navegar a una ruta de prueba de `facebook.com` en una pestaña nueva y aislada de Chrome.
4. Corroborar la decisión en Activity y la pantalla bloqueada del navegador.
5. Cerrar sólo la pestaña creada por la prueba y terminar la sesión mediante la confirmación normal de React.

## Resultado

El Host React .112 PID 29404 inició desde la UI una sesión normal de dos minutos con perfil `2`, nueve sitios Block, `Strict=false` y enforcement conectado. El servicio instalado LocalService PID 13424 reportó `Active`, `ExtensionReady=true` y `EnforcementConnected=true`.

Una pestaña nueva en el Chrome existente navegó al destino exacto `https://facebook.com/focusguard-acceptance-fg340`. La extensión redirigió a su página local y el servicio registró el evento ID `62887c01-7022-415a-ab01-f962b0fed802`, `kind=1`, `outcome=Blocked`, bajo SessionId `dde39488-6021-4ca1-badc-a2068b14711b`. La página mostró «facebook.com will still be there later» y la cuenta regresiva. No inició sesión ni cargó el contenido de Facebook.

La pestaña temporal se cerró; Chrome volvió a sus cuatro pestañas anteriores y no conserva ninguna pestaña de la prueba. React presentó la confirmación «¿Detener esta sesión normal?»; al confirmarla, el servicio cambió a `Stopped`, `remainingSeconds=0` y `EnforcementConnected=false`, mientras Service/Extension siguieron listos y Strict apagado.

## Evidencia

- `artifacts/fg340-output-react-browser-session.json`: perfil, consentimiento normal y status activo.
- `artifacts/fg340-output-react-browser-acceptance.json`: URL de prueba, valor de navegación exacto, evento Blocked y estado activo. Los nombres ajenos de pestañas se retiraron del JSON.
- `artifacts/fg340-output-react-browser-facebook-block-content.png`: recorte de la pantalla bloqueada sin barras ni títulos de otras pestañas.
- `artifacts/fg340-output-react-browser-cleanup.json`: confirmación de parada normal y status terminal.

## Seguridad, UX y límites

Se usó una sesión temporal normal; nunca se habilitó modo estricto. La prueba abrió una pestaña nueva, comprobó la URL antes de navegar, no interactuó con cuentas y cerró esa pestaña al terminar. No cambió reglas, perfil ni preferencias guardadas. El servicio siguió ejecutándose como `NT AUTHORITY\LocalService`.

La pantalla de bloqueo conserva el dominio y explica cuándo volverá a estar disponible. El texto se presentó en inglés en Chrome aunque la UI React estaba en español; la paridad de idioma del Native Messaging/extension page queda por revisar. Esta aceptación cubre Chrome .112 y `facebook.com`; Edge, otros dominios, .116 y el arreglo FG-337 de ACK todavía no están aceptados en la instalación vigente.

## Siguiente paso

FG-341 ya acepta bloqueo en Edge con la extensión .98. Alinear Edge con el paquete .112/.116 e instalar/validar .116 cuando una consola elevada esté disponible. Mantener pendientes el handshake Agent independiente con política activa, reinicio/rollback instalado, DPI/Windows 11, accesibilidad de pantalla y los gates de Fases 8–10.
