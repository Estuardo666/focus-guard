# FG-353 — Ciclo estricto con excepciones de descanso fallaba al activarse (HIGH RISK) y pulido de formulario

**Fecha:** 2026-10-09 local  
**Fase:** 8 (strict/ciclos) y 9  
**Estado:** corregido en servicio y UI, empaquetado e **instalado (.122)** con autorización explícita del propietario; ciclo estricto aceptado de extremo a extremo.

## Defecto encontrado en el instalado

Al activar desde React un ciclo estricto (Enfoque 1 / Descanso 1 / 2 ciclos, solo temporizador) el servicio terminó en **`Failed`** sin cuenta atrás ni fases. Causa raíz, reproducida con una prueba:

- `CycleIpcProtocol` y el resto del producto (FG-216) exigen que las reglas de descanso sean **`Block`**.
- `StrictPolicySnapshotStore.Validate` (persistencia estricta) exigía **`Allow`** para esas mismas reglas, y lanzaba `InvalidDataException: The strict policy rules are invalid`.
- `InMemoryFocusService.ActivatePreparedSessionAsync` capturaba la excepción sin registrarla y devolvía «Strict persistence failed». **Todo ciclo estricto con un perfil que tenga excepciones de descanso fallaba**: la sesión queda `Failed`; falla cerrado (no hay bloqueo estricto, pero tampoco sesión).
- Por qué no lo vio ninguna prueba: la prueba de servicio usaba `RecordingStrictSessionStore`, que acepta cualquier política, y la de persistencia usaba reglas `Allow` (semántica anterior a FG-216). Es la clase de mock que oculta validación real.

## Cambios

- `StrictPolicyPersistence.cs`: las reglas de descanso se validan por acción definida (`Allow` o `Block`); la semántica Block-only queda en el ingreso (`CycleIpcProtocol`). Se sigue aceptando `Allow` para no invalidar snapshots de versiones anteriores.
- `InMemoryFocusService.cs`: la excepción de persistencia estricta se registra (`ServiceFailureLog`: tipo y traza, nunca mensaje ni credencial) en vez de tragarse.
- Pruebas: `StrictPolicyPersistenceTests` pasa a `[Theory]` Allow/Block (el caso Block **falla antes**); nueva `StrictCycle_WithBlockBreakExceptions_ActivatesThroughTheRealStrictStore` con `StrictSessionStore` real (**falla antes** con «Strict persistence failed.», pasa después).
- UI: el botón de revisar deshabilitado explica el motivo (duración fuera de rango o valores de ciclo inválidos; antes quedaba sin mensaje y «Descanso largo cada…» exige ≥ 2); la revisión estricta ya no afirma que ciertas apps/sitios «seguirán bloqueados durante los descansos» cuando el bloqueo está desactivado; las acciones de capturas ya no quedan deshabilitadas tras terminar una sesión estricta por el `strictMode: true` residual del estado (la lista de estados ya excluía las sesiones activas; prueba que falla antes).

## Validación

- `dotnet test FocusGuard.slnx -c Release`: todos los proyectos pasan (Service 112, Host 154, Client 111, Storage 40, Desktop 189, UiTests 15, Contracts 62, Ipc.Windows 79, ProcessAgent 26, NativeMessaging 23, Domain 39, entre otros). React **134/134**; `npm run build` y los contratos pasan.
- Setup .122: `artifacts/installer/FocusGuard-0.1.122-Setup.exe`, 442857656 bytes, SHA-256 `637733F35DFE83256D6582CC364557BC8E89EDC632FCA5FB3A3427C405A1AA10`; ZIP SHA-256 `9C5706A1D8A9AE9DA487B87C8B9D0C9FE6ADF62160B3B6A62E7399DCC29CA041`; payload SHA-256 `3B393749767771D714D65E7AE2FEBDFF78A4873AB3543C594AE24AF00446310A`. manifest 31/31; UI del `dist` idéntica al payload; paquetes Chrome/Edge e instalador pasan.
- Instalación: el preflight rechazó la primera vez («requires a ready service with no active or strict session») porque el estado conservaba la sesión estricta `Failed`; se restableció con una sesión normal de 1 min y el preflight pasó. Después: extensión 0.1.122, **8/8 binarios (incluido el servicio) y 18/18 recursos UI idénticos**, servicio `LocalService`, política IPC con el mismo owner y consentimiento. Log: `artifacts/fg353-install-0.1.122/install.log`.
- **Aceptación instalada del ciclo estricto:** revisión con las tres fases (Enfoque 8:01–8:02, Descanso corto 8:02–8:03, Enfoque 8:03–8:04) → activación → `Active` con `strictMode=true`; la UI mostró «Ciclo 1 / Enfoque», pasó a «Descanso corto» y a «Ciclo 2 / Enfoque» en las horas previstas; «Saltar descanso» quedó deshabilitado; la sesión expiró sola (`Expired`). No se imprimió ni guardó ningún código de recuperación.

## Observaciones

- **Actualización y extensión:** tras instalar .122, `extensionReady` quedó en `false` durante más de 9 minutos. Chrome había lanzado el host de mensajería nativa en el mismo segundo en que el instalador reescribía `ipc-policy.json`. Terminar ese proceso (Chrome lo relanza solo) devolvió `extensionReady=true` al instante; reiniciar el servicio **no** lo rompe (el host sigue y se recupera). Hipótesis: un host lanzado durante la ventana de actualización queda atascado. No se reprodujo ni se investigó el código del host. Posible mejora de instalador: terminar al final los hosts nativos lanzados durante la actualización.
- Tras terminar una sesión estricta, el estado del servicio conserva `strictMode: true` (y `Failed` si falló) hasta que empieza otra; eso bloqueó el preflight de instalación. El Host sigue mostrando «Sesión actual · Modo estricto · Sesión finalizada» tras expirar. Diseño por decidir: confirmar la liberación o limpiar el indicador al terminar.
- Las pruebas .NET que provocan el fallo estricto escriben en el `service-failures.jsonl` real de `ProgramData` (`ServiceFailureLog` no está aislado en tests): la entrada de 07:49:57 viene de mi ejecución de la prueba sin el arreglo, no de un fallo del servicio instalado.

## Pendiente

Extensión Edge .122, notificaciones/bandeja con UI cerrada, Windows 11, DPI/monitor real, Narrator/forced-colors, reboot/recovery real (con espera de 15 min), carga sostenida, contrato de horarios de 1 min y Fase 10 (Avalonia se mantiene).
