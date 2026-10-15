# FG-418 Rediseño premium de cuenta, portal y tema Keycloak

## Resumen
- App (Conexión): AccountPanel con hero de identidad (avatar con iniciales, estado en pastilla, acciones jerarquizadas primaria/secundaria/discreta), franja de confianza (correo, dispositivo, cifrado E2E) con iconos Radix, y tarjetas de sección para solicitudes de dispositivos y transferencia de ajustes. Se ocultó visualmente el título duplicado `.connection-intro` (sigue para lectores de pantalla).
- Pase de oficio (Emil/taste): curva `cubic-bezier(.23,1,.32,1)`, `scale(.97)` al pulsar, hover sólo con puntero fino, entrada `@starting-style` con escalonado corto, sin `filter: blur` costoso; `prefers-reduced-motion` deja sólo opacidad.
- Portal Vercel: lista de garantías, avatar, pastilla de estado, datos en tarjetas, botones con feedback; "Se vinculan desde la app" en lugar de afirmar estado no conocido por el portal.
- Keycloak: tema `focusguard` (login hereda keycloak.v2, account hereda keycloak.v3) sólo CSS; añadido al Dockerfile y realm-template. Script nuevo `cloud/deploy/Set-KeycloakTheme.ps1` activa el tema vía Admin API sin imprimir secretos.

## Comandos y resultados
- `npx tsc --noEmit`, vitest UI completa: 38 archivos / 235 tests PASS.
- Portal `node --test tests/secure.test.mjs` 5/5; `Build-Portal.ps1` PASS (espejo NTFS); `vercel deploy --prod` OK. `https://focusguard.totemmassmedia.com/` 200 con el nuevo markup.
- Cloud Build está deshabilitado en `tnc-staging-2026` (no se habilitó). Imagen construida con Docker local y publicada: `us-east4-docker.pkg.dev/tnc-staging-2026/tnc-staging/keycloak@sha256:b74c93cb9a2d805b74c9d08f1261ae3e2678a1fe017380208087491ae6f10eb4`. Rollback: digest previo `sha256:c7e71e16b8ea82e1c7796d3bc2797ee444b6f5f118025d324a53cd92d0042e23`.
- `gcloud run services update tnc-identity-staging` → revisión `tnc-identity-staging-00002-ht5` al 100 %.
- `Set-KeycloakTheme.ps1`: antes login/account vacíos (por defecto), ahora `focusguard`/`focusguard`. Página de login sirve `focusguard.css`; consola de cuenta 200. Rollback: `Set-KeycloakTheme.ps1 -Theme keycloak.v2` o vaciar el campo en la consola admin.
- Instalador 0.1.156 (versión de extensión/UI subida a 0.1.156): build, self-test, smoke de lanzamiento no elevado, paridad Chrome/Edge y tests de script PASS. Setup SHA256 `75C7027E5538D50F9BD665DBF2C82DA13C7AB9A8C7BCDAE84EF8F35002DE7867`; ZIP `668CA2ACB118B548584217175AF320189C935E3229534CBF6CBAF67BB9B75837`.

## UX / Seguridad
- Jerarquía clara; estados no dependen sólo del color; forced-colors y reduced-transparency cubiertos. Sin nuevas superficies de datos; el tema Keycloak es sólo CSS (sin plantillas FTL ni JS).

## Limitaciones
- Aceptación visual pendiente: WebView2 instalado (.156 no instalado), portal y Keycloak en navegador real, DPI/forced-colors. La consola de cuenta v3 sólo admite recolor; su estructura sigue siendo la de Keycloak.

## Siguiente
- Owner instala .156 y revisa Conexión; captura de login/cuenta Keycloak.

## Ronda 3 (feedback del owner sobre .156)
- Corregido el hueco vacío del hero: `.account-hero>*{position:relative}` anulaba el `absolute` del brillo decorativo, que ocupaba 360 px.
- Ancho uniforme 880 px para el panel de cuenta y las tarjetas de estado de Conexión.
- "Cerrar sesión" con estilo rojo suave (`--danger` nuevo, variante clara accesible).
- Barra lateral: `AccountChip` (avatar con iniciales, nombre/correo, punto de vínculo) que abre Conexión; oculto sin sesión y en ancho móvil. Test nuevo en AccountPanel.test.tsx (8/8); UI completa PASS.
- Portal y Keycloak reestilizados con los tokens aurora de la app (bg #0c0b12, glass, acento #cdb8ff) y el orbe; DESIGN.md del portal pendiente de alinear con este cambio. Verificación visual local del portal (next start) correcta.
- Instalador 0.1.157: build + tests PASS. Setup SHA256 `8C32036E4BA3281E43475B5E4AE8A51EC5C368684256855D1AB26171B997CE44`.
- Desplegado: portal Vercel prod (200, orbe presente) y Keycloak `keycloak@sha256:12a49a52f3e9faa29720274e5fdd4be43100e1bf4405d06317efcb8722633894`, revisión `tnc-identity-staging-00003-m9n` 100 %; login sirve CSS aurora. Rollback: digest `sha256:b74c93cb…` (rev 00002). Pendiente aceptación instalada de .157 y revisión visual en navegador real.
