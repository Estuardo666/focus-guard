# FG-246 — Métricas del historial de ciclos y paquete 0.1.82

## Resultado

FG-144 queda implementado: el historial cuenta ciclos de foco y descansos que terminaron completamente, calculados desde el plan de fases autoritativo y el instante real de fin. Las fases parciales, futuras o todavía activas no se cuentan. Las sesiones de timer y los registros legacy cuyo consentimiento de enforcement se desconoce conservan métricas nulas; no se inventan valores al leer registros antiguos.

Las métricas opcionales pasan por servicio, historial SQLite, protocolo compatible hacia atrás y resúmenes Classic y React, con etiquetas en inglés y español. SQLite agrega una columna nullable; la migración de esquema antiguo conserva sus filas con métricas desconocidas. Los contratos rechazan métricas fuera de rango. Las pruebas cubren límites de fase, round-trip/migración, payload legacy, Host, ViewModel y UI.

## Verificación ejecutada

- `dotnet test FocusGuard.slnx -c Release --no-restore --logger 'console;verbosity=minimal'`: exit 0, **870/870**.
- React: **102/102**; `npm run build` incluye TypeScript y pasa el verificador de distribución.
- La comprobación del build informa bordes editables de **3.46:1** (oscuro) y **3.67:1** (claro), texto/CTA de **6.20:1** y **4.94:1**, y `CanvasText` en forced-colors.
- Extensión: **19/19**; hashes y entradas necesarias de los ZIP Chrome/Edge .82 validados.
- `scripts/test-installer.ps1`: pasa.
- Host empaquetado .82: `--smoke`, `--smoke-renderer-failure` y `--smoke-renderer-crash`, todos exit 0.
- El ZIP .82 abre, contiene manifest versión .82 y 1,732 entradas; los 18 archivos de UI empaquetados coinciden por SHA-256 con el `dist` recién compilado. Setup: 442,828,984 bytes; ZIP: 699,127,343 bytes. Hashes completos en `D:\TEMP\focusguard-fg166-validation\artifacts\fg246\build-receipt.json`.
- Graphify: 10,198 nodos, 27,559 aristas, 980 comunidades.

## Límites y siguiente paso

El paquete .82 no se instaló; el Host instalado continúa en .79 y el token de Codex no está elevado. No se ejecutaron en .82 el `--package-self-test` de Setup ni su smoke gráfico. Por eso la inspección visual y de teclado de métricas/contraste/presets en WebView2 sigue pendiente en FG-242. FG-145 conserva sus pruebas de reinicio y recuperación controlada.

La evidencia que compartió el usuario para FG-235 contiene dos `RequestTimeout` con `OperationCanceledException` a las 12:24Z, no una excepción de terminación cerca del 7034 de las 19:35Z. `ipc-control-rejection.json` no existe; esos datos no establecen la causa del 7034. Se conserva el diagnóstico de FG-235 sin atribución causal.