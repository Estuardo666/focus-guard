# FG-230 — Respetar el alto cliente compacto plegado

## Evidencia

El Host0.1.76 pidió `ClientSize=480×220` DIP, pero la ventana exterior capturada midió 496×280 px. `MainForm` aplica `MinimumSize.Height=280` como medida exterior; ese mínimo clampa el ClientSize solicitado, dejando visible el contenedor más alto de lo especificado.

## Plan

1. Al entrar a compacto, medir el chrome exterior/cliente real de la ventana actual y sumar ese valor al mínimo exterior necesario para un cliente de 220 DIP.
2. Extender el test Host nativo para cubrir que el mínimo exterior corresponde al alto cliente plegado más chrome, conservando expand/collapse/full restoration.
3. Ejecutar Host tests, React tests si el code no cambia, build Release, test-installer, Graphify y generar package0.1.77 fuera del repo.
4. Abrir el Host0.1.77 temporal sin `--pilot`, plegar detalles y verificar 480×220 DIP cliente (la captura Win32 exterior será chrome+220); cerrar sólo el PID lanzado.

## Límites

No tocar sesión, servicio, IPC, perfiles, datos o instalación. Un smoke temporal no cuenta como DPI/per-monitor ni aceptación de servicio instalado.

## Resultado

Implementado el 2026-10-06. El mínimo exterior ahora se calcula como `ClientSize` plegado más chrome no-cliente real medido al entrar al compacto. Host tests149/149; `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings/0 errors; `scripts/test-installer.ps1` pasa. Setup `--package-self-test` exit0; manifest31/31, ZIP1732; Graphify10155 nodos/27380 aristas/997 comunidades.

Setup0.1.77 SHA-256 `5129731D5382966ACD11B2C037804BEB8F5323C4501E7CEC1DA310E5A24E6DA1`; ZIP SHA-256 `C8C8241DE2D1F1ED7854366464A4ED8F752B757BA17D7A6D7CE4BD5B917447B8`. Host0.1.77 temporal plegado, sin `--pilot`: Win32 `GetClientRect=480×220`, `GetWindowRect=496×259`; captura `D:\TEMP\focusguard-fg166-validation\artifacts\fg230\react-host-0.1.77-compact-collapsed.png`. Cerrado el PID de QA. No se instaló el Setup.
