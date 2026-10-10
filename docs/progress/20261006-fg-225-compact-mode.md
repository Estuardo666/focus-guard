# FG-225 — Modo compacto del Host React

## Resultado

El mismo Host cambia entre la interfaz completa y una tarjeta compacta de 480×340 DIP. La barra nativa de Windows permanece visible para mover la ventana. El atajo `Ctrl+Shift+M` y el botón de la página alternan inmediatamente; `Siempre visible` controla `Form.TopMost`. Al volver a la vista completa se restaura el tamaño, bounds y estado maximizado anteriores. Cerrar el Host continúa sin enviar stop.

La tarjeta usa exclusivamente lecturas existentes: tiempo de fase/temporizador de `status.get`, nombre del perfil desde la intención de sesión cuando se conoce, fin exacto y estado estricto. Marca lecturas stale y permite refrescar; advierte cuando el bloqueo fue solicitado pero no está confirmado; en Strict/RecoveryPending ofrece regresar a instrucciones de recovery en la vista completa. No expone Start/Stop, edición, reglas ni operaciones de enforcement.

El nuevo canal bridge `window.compact.set` / `window.alwaysOnTop.set` exige el origen local exacto, token del documento vigente, parámetros exactos booleanos, límite compartido de peticiones y respuesta tipada. Se maneja antes de la autorización de operaciones de servicio y no consulta el cliente del servicio; `MainForm` cambia sólo su propia ventana. El evento `host.windowState` está ligado al token para que el atajo nativo, la recuperación del renderer y los botones React se mantengan sincronizados. Classic conserva el único propietario de bandeja.

Diseño aplicado desde los cinco recursos solicitados: Geist y graphite/mint existentes; jerarquía breve y densa de utilidad de escritorio; destinos de 44 px, nombres accesibles y focus visible; label del atajo; focus transfer al cambiar vistas; título nativo arrastrable; cero animación en la acción frecuente; y reducción de movimiento aplicada a preferencias CSS. `motion/react` era usado sólo por una transición de opacidad sin efecto de producto y se retiró; el bundle inicial baja de 501.12 KB a 378.59 KB. `CompactFocus.js` se carga como chunk local CSP-allowlisted (4.07 KB), incluido en el paquete.

## Verificación

- `npm run check`: correcto.
- `npm test`: 20 archivos, 92/92 pruebas aprobadas. Incluye stale, ciclo/descanso, bloqueo no confirmado, Strict/RecoveryPending, foco y mensajes con token/documento exactos.
- `dotnet test tests/FocusGuard.Host.Windows.Tests/FocusGuard.Host.Windows.Tests.csproj -c Release --no-restore`: 148/148 aprobadas. La regresión del Form comprueba tamaño compacto/restauración, TopMost y cero consultas al servicio; bridge verifica bools exactos, rechazo de parámetros extra y bypass de autorización/IPC de sesión sólo para operaciones locales de ventana.
- `dotnet build FocusGuard.slnx -c Release --no-restore`: correcto, 0 warnings / 0 errors; TypeScript, Vite y verifier CSP se ejecutan en el build.
- `scripts/test-installer.ps1`: pasó.
- `graphify update .`: 10.102 nodos, 27.211 aristas, 975 comunidades; `graph.html` se omite por el límite visual del grafo.
- Setup standalone 0.1.73: manifiesto 31/31 recursos presentes; ZIP menú 1.732 entradas. Setup externo e incluido coinciden por SHA-256 `2341BC295341D264B408BE60C6DD57F524CEADFD79808B8715F8F76D31B0CE76`; ZIP SHA-256 `D776C1B0076C34F4F76D8793929F2EC3A18FE96FD1D3CEAC444A97708EC6B9D3`. `CompactFocus.js` E `dist` y paquete coinciden: `C7A644B987D81B7A8EAABEAD89B24DA5262BAE720A74A281E5976347857B6A76`.

## UX, seguridad y limitaciones

La vista compacta conserva exactamente el estado recibido del servicio; no ejecuta un reloj local ni ofrece acciones de enforcement en una superficie pequeña. Los avisos de protección/recovery son visibles y tienen ruta a la vista completa. No se implementaron bandeja ni toasts nativos y no hay segundo propietario de tray.

El paquete no se instaló: la ejecución actual no tiene token elevado. Tampoco hay `orca` disponible para captura automatizada. Por ello quedan pendientes la captura visual instalada de 0.1.73, Windows/DPI multi-monitor y el ensayo manual del drag/atajo en WebView2 empaquetado. La validación de componentes y el test de bounds nativo no sustituyen esas pruebas.

## Siguiente paso

Completar F223 con un browser runner permitido; después validar visualmente 0.1.73 en WebView2 instalado. Plegado de detalles de Classic continúa como FG-226. Bandeja y notificaciones nativas permanecen como subpasos de Phase8 sujetos al handoff de ownership con Classic.
