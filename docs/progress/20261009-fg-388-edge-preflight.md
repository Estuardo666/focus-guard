# FG-388 — preflight Edge .135

## Resultado (2026-10-09)
- Edge .135 is installed and running in background, but FocusGuard was absent from the existing `Default` and `Profile 1` extension folders.
- I launched an isolated temporary Edge user-data directory on D: with the unpacked FocusGuard extension .135. `edge://extensions` showed FocusGuard loaded and enabled.
- Edge automatically associated the temporary profile with the signed-in Microsoft account and presented its sync dialog. I did not accept it or navigate to any site.
- I retried the same isolated profile with `--disable-sync`; the sync dialog no longer appeared, but the new tab displayed personal bookmarks. I did not navigate. Guest mode displayed “Extensions is not available when browsing as a guest.”
- All Edge windows using the temporary profile are closed; no `msedge.exe` process references it. The profile remains at `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\edge-profile` (1,830 files / 526,557,527 bytes).
- A recursive removal of this exact temporary profile was rejected by automatic approval review with the stated reason “blocked by policy”. No alternate deletion method was attempted. The browser-block test remains pending.
- Evidence: `D:\TEMP\FocusGuard-FG386-scheduled-intent-20261009\edge-preflight-status.json`.
## Continuación (2026-10-09)

- El staging instalado en `C:\Program Files\FocusGuard\browser-extension\staging` tiene versión `0.1.135` e ID `emhdinkjpinjnmldipnejnbeefgonjnj`; el host Edge instalado permite ese origin. No encontré ese ID en `Preferences` de los perfiles Edge enumerados.
- Se observó un `FocusGuard.NativeMessaging.exe` (PID 34856) cuya cadena de padres incluye `msedge.exe` (PID 24708). Esto prueba que Edge lanzó el native host, pero no identifica qué extensión/perfil lo invocó ni prueba bloqueo de navegación.
- La revisión automática rechazó el intento de lanzar Edge aislado con `--disable-sync`, `--load-extension` del staging .135 y `--app=https://focusguard.invalid`, con «blocked by policy». El proceso de lanzamiento no se creó; no hubo navegación. No se intentó una vía alternativa.
- Para preparar la prueba, creé desde React el perfil `FG392 Edge browser test` (solo Block `focusguard.invalid`) y luego lo eliminé desde su confirmación de perfil. La UI regresó a los cinco perfiles originales, 5 reglas de aplicación y 30 sitios; `desktop-settings.json` conserva el SHA `987E6001B6CA83565C4367C8BC1C82D511CDA5AA6654F5C22A5C8A79BE0C2D55`. No se inició sesión y no se abrió ningún sitio.
- El perfil temporal D: creado en el preflight anterior sigue guardado; no hay Edge que lo use. Su borrado recursivo anterior continúa rechazado; no se buscó otro método.
