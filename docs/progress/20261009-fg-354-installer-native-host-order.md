# FG-354 — Instalador: registrar el host nativo solo cuando el servicio nuevo responde

**Fecha:** 2026-10-09 local  
**Fase:** 9 (packaging/update)  
**Estado:** cambio de instalador probado por contrato, empaquetado e **instalado (.123)** con autorización explícita del propietario. La causa raíz se deduce del orden del script; **el fallo observado no se reprodujo** tras el cambio.

## Observación (FG-353)

Tras instalar .122, `extensionReady` quedó en `false` más de 9 minutos. Chrome había lanzado el host de mensajería nativa en el mismo segundo en que el instalador reescribía `ipc-policy.json`; terminar ese proceso (Chrome lo relanza solo) devolvió `extensionReady=true` al instante, y reiniciar el servicio no lo rompe.

## Análisis

En `scripts/install-focusguard.ps1` el registro del host (`HKCU\...\NativeMessagingHosts\com.focusguard.native`, Chrome y Edge) se recreaba **antes** de escribir `ipc-policy.json` y de iniciar el servicio:

1. `Suspend-FocusGuardNativeHostRegistration` quita el registro al empezar (Chromium reconecta el puerto nativo ~1 s después de cualquier desconexión, y el propio script lo documenta).
2. Copia de componentes, accesos directos y **registro del host (líneas ~815–818)**.
3. Política IPC, `Start-Service`, comprobación de salud.

Entre 2 y 3 hay una ventana en la que el servicio está parado o la política es antigua; un host lanzado por Chromium justo al reaparecer el registro arranca contra ese estado. Es coherente con lo visto, pero es una hipótesis: no se investigó el código del host para confirmar que no reintenta.

## Cambio

- `install-focusguard.ps1`: el bloque de registro se mueve a después de `Assert-FocusGuardServiceResponsive` (antes de lanzar Desktop/Agent). El rollback no cambia: sigue restaurando `previousHostRegistrations`.
- `test-installer.ps1`: nuevo contrato que falla si el registro no sigue a la comprobación de salud del servicio nuevo (el contrato previo, registro después de la copia, sigue vigente). Pasa con el orden nuevo; no se ejecutó una variante que lo rompa.

## Validación

- `scripts/test-installer.ps1`, `test-browser-extension-package.ps1 -Version 0.1.123`: pasan. El `install-focusguard.ps1` del paquete es idéntico al del repositorio.
- Setup .123: `artifacts/installer/FocusGuard-0.1.123-Setup.exe`, 442857656 bytes, SHA-256 `0CD3A4D3457484BE992894FB784815890C863DF9535386AA1CF11A2472BE39B8`; ZIP SHA-256 `35C0463BC4648D8E7F8BECBA86E960BB9AC182D76003CF058243FBF64D3985D6`; payload SHA-256 `109D89174AD81FACC49343159BA0EDE5A3CE7C8237E9332474C99401781EC523`; manifest 31/31.
- Instalación (preflight: instalado .122, `Inactive`, Strict off): extensión 0.1.123, 8/8 binarios y 18/18 recursos UI idénticos, servicio `LocalService`. Durante 90 s tras instalar, `extensionReady` se mantuvo `true` sin intervención. Log: `artifacts/fg354-install-0.1.123/install.log`.

## Límite

En esta instalación **ya había un host nativo vivo** al empezar, a diferencia de la de .122 (donde Chrome lo lanzó durante la actualización), así que no se reprodujo la condición que falló. El resultado es compatible con el arreglo pero no lo demuestra. Para demostrarlo hay que repetir una actualización partiendo de Chrome sin host nativo activo (sin pestañas con la extensión despierta) y comprobar `extensionReady` sin intervención.

## Pendiente

Demostrar el escenario sin host previo; extensión Edge; el resto de pendientes del plan.
