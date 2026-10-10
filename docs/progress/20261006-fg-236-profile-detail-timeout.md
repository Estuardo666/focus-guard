# FG-236 — Detalle de perfil no bloqueado por el catálogo de aplicaciones

## Problema observado

La captura del Host React .77 instalado mostraba `Estado actualizado` y la lista de perfiles, pero el espacio del perfil seleccionado permanecía en `Cargando perfiles…`. El puente tipado asigna tres segundos por operación; el cliente React deja su request de detalle en espera cuatro segundos.

## Causa confirmada en el código y reproducción

`NativeBridgeDispatcher.HandleProfileCommandAsync("profile.get")` llamaba `EnsureApplicationCatalogAsync` antes de devolver las reglas guardadas. Ese helper enumera hasta 1.000 accesos directos del menú Inicio y resuelve sus destinos vía `WScript.Shell`/COM. El catálogo sólo es necesario para buscar y presentar aplicaciones instaladas, no para leer la configuración ya guardada.

La prueba nueva `ProfileDetailsDoNotWaitForOptionalApplicationDiscovery` usa un catálogo que no responde. Antes del cambio, `profile.get` agotó exactamente el deadline de tres segundos y devolvió un error en vez del perfil. Después del cambio devuelve el detalle sin llamar al catálogo. Cuando el catálogo ya se cargó en el documento actual, las reglas siguen usando nombres, rutas e iconos coincidentes; sin catálogo, la respuesta deja `presentation` nula y React muestra el valor exacto guardado y su tipo, sin inventar una coincidencia.

## Cambio y límites

- Se quitó el descubrimiento de Start Menu del camino crítico de `profile.get`; `applications.list` sigue siendo quien inicializa el catálogo al usar la búsqueda de aplicaciones.
- No cambia almacenamiento, semántica de reglas, política de bloqueo, autorización ni protocolo IPC.
- Se subió UI y extensión de .1.77 a .1.78 y se generó el instalador standalone.
- Se conservaron el contrato visual y las guías solicitadas (UI/UX Pro Max, Emil, Taste, Front-end Design y Apple Design). Este cambio despeja el panel de perfil cuando la búsqueda de aplicaciones aún no se ha usado; no cambia colores/espaciado y la revisión visual sigue abierta.
- Los SHA-256 instalados de Service, Desktop, `FocusGuard.UI.exe` y `FocusGuard.UI.dll` coinciden con el paquete .78. El owner health check registró `Ready` a `2026-10-06T20:33:17Z`; el servicio está `RUNNING` PID28016 LocalService. El Host .78 abrió como usuario normal, sin `--pilot` ni sesión activa.
- PrintWindow capturó aisladamente el Host .78, con `Estado actualizado`, perfil seleccionado, identidad y catálogo de aplicaciones; desapareció `Cargando perfiles…`. No se editó ninguna regla ni se inició una sesión. Imágenes: `D:\TEMP\focusguard-fg166-validation\artifacts\fg236\host-0.1.78-profiles-final.png` y `host-0.1.78-profiles-rules3.png`.
- La revisión del perfil confirma la jerarquía identidad → catálogo/reglas con estado de servicio visible; no se cambió CSS. A 1216×799, Sitios y Excepciones quedan más abajo en el scroll y aún deben revisarse; en Resumen el formulario completo y `Revisar sesión` quedan bajo el primer pliegue. DPI/per-monitor, compacto y Narrator siguen abiertos. El CLI Orca no está disponible; los snapshots PrintWindow se obtuvieron tras confirmar que FocusGuard era la ventana frontal.

## Verificación

- Regresión antes del fix: `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ProfileDetailsDoNotWaitForOptionalApplicationDiscovery` falló con error `ServiceUnavailable` después de3s.
- Después del fix: Host.Windows.Tests150/150; `dotnet test FocusGuard.slnx -c Release --no-restore` 851/851.
- UI React95/95 y TypeScript/build local pasan; extensión19/19 y typecheck; `test-installer.ps1` y `test-browser-extension-package.ps1 -Version 0.1.78` pasan.
- Setup0.1.78 `--package-self-test` exit0. SHA Setup `14CF3DA35C6A45C7D41AAF9076E229124EC605A200588DB3188D53C8E4F11039`; ZIP `1E5202BD73DDC38EDF6C3913BB7F9BD620B82BF8C44B0E3839CC49E56E7534BF`.
- Artefactos: `D:\TEMP\focusguard-fg166-validation\artifacts\fg236\FocusGuard-0.1.78-Setup.exe` y `FocusGuard-0.1.78-win-x64-menu.zip`.
- Recibo con hashes y versiones: `D:\TEMP\focusguard-fg166-validation\artifacts\fg236\build-receipt.json`; Host.Windows `FocusGuard.UI.dll` SHA-256 `4510949A7F65571DA72800F9DDCF3EB46CC409FD4B0CE95A6579930735463A40`.

## Siguiente paso

Completar la revisión visual de Sitios/Excepciones y de la acción primaria del Resumen a 1216×799; aplicar sólo ajustes respaldados por las cinco skills y capturar antes/después. Después verificar compacto, DPI/per-monitor, Narrator y bloqueo web con Host .78.
