# FG-227 — Acceso al piloto React desde la bandeja Classic

## Resultado

El flyout del tray Classic presenta `React pilot` junto al modo compacto Classic, y el menú contextual del mismo icono ofrece la misma acción. La etiqueta identifica explícitamente el piloto y el tooltip recuerda que Classic permanece en la bandeja. No se crea otro icono y `Open FocusGuard` sigue mostrando la interfaz Classic.

El launcher resuelve `FocusGuard.UI.exe` hermano de la instalación y lo inicia con `--pilot`, `UseShellExecute=false` y el working directory de instalación. Nunca eleva ni envía comandos al servicio. Si la misma ruta ya está abierta en la sesión actual, intenta restaurarla/activarla en vez de crear un segundo proceso; si falta el Host o no se puede iniciar, Classic muestra un toast localizado.

## Verificación

- Desktop unit tests: 188/188; incluyen argv/workdir/sin shell, proceso existente, Host ausente/fallo, copy ES/EN y una sola definición `TrayIcon`.
- Dos assertions previas de ciclo fallaban porque el `MemoryProfileStore` de prueba sólo implementaba la interfaz legacy; el fixture ahora ejerce el CAS revisionado y ambas pasan. No se cambió la lógica de presets del producto en este launcher.
- La suite Desktop fallaba previamente en dos escenarios de presets porque `MemoryProfileStore` de prueba sólo implementaba la interfaz legacy; el fixture ahora implementa CAS revisionado y ambos escenarios pasan sin cambiar el comportamiento del producto.
- Desktop UI tests: 15/15.
- React: 93/93; Host: 149/149.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: 0 warnings / 0 errors.
- `scripts/test-installer.ps1`: pasó.
- `graphify update .`: 10.150 nodos, 27.328 aristas, 978 comunidades; visual HTML omitida por límite de 5.000 nodos.
- Setup standalone 0.1.75: manifiesto 31/31, ZIP menú 1.732 entradas. Setup externo/incluido SHA-256 `8C752B685DBAFD7F06F58934C344B4A7D1984247C1DAB80306DFCFAEC08178AA`; ZIP SHA-256 `97C1D1340F8EEB1FB6D3B029ADF5206EEA8C6E95CBADAFCF55F496A2EFEAB107`. Desktop EXE en el payload: SHA-256 `DDC1D897E3E5230F525F77A61D838A3CB3B1BDB4A09EF4698504694A27D3E3AC`.
- Se preservó intacto el Setup 0.1.74 de FG-226.

## UX, seguridad y límites

La acción está junto al compacto Classic y exige un click explícito para abrir el Host en modo piloto. La bandeja sigue siendo propiedad de Classic; React no incorpora un `NotifyIcon`. El launcher usa una ruta fija, un solo argumento constante, `UseShellExecute=false` y se ejecuta con el token del usuario. El CAS existente protege la edición de perfiles si ya se está usando otra interfaz.

El paquete 0.1.75 no se instaló porque el proceso actual no está elevado. Faltan la prueba instalada del recorrido bandeja→Host, la captura visual del menú a distintos DPI y la aceptación browser F223. El launcher se validó con tests/fakes; no se afirma que ya esté operativo en el equipo.

## Siguiente paso

Instalar/validar 0.1.75 cuando exista un token elevado, confirmar apertura del Host desde el tray como usuario normal y probar el caso «ya abierto». Luego completar F223 con una vía permitida que use la extensión FocusGuard real. Notificaciones nativas cuando Classic no esté ejecutándose y el handoff del tray quedan abiertos.
