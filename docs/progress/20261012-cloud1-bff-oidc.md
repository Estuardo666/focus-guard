# CLOUD-1 — Checkpoint 3: OIDC API y BFF integrados en ensayo

Fecha: 2026-10-12 (America/Guayaquil). Estado: **en curso**, no es cierre de Fase 1.

Actualización: restauración/rotación de tokens e integración Host/React avanzaron en [checkpoint 4](20261012-cloud1-desktop-account.md); este documento conserva el resultado de la integración OIDC/BFF.

## Resultado

Se completó una prueba vertical real del cliente OIDC, la API y el portal/BFF contra Keycloak 26.8.0 y Neon con TLS. El ensayo finalizó con exit code 0: 55 checks de identidad/API, 16 checks del BFF y persistencia del realm después de reiniciar el contenedor.

El BFF completa authorization code + PKCE, valida state/nonce, persiste una cookie opaca y una carga AES-GCM, y sirve sólo el estado sanitizado. Una cuenta sintética con correo no verificado queda diferenciada y no se consulta como cuenta validada por la API. El cierre rechaza CSRF inválido sin cerrar la sesión; con CSRF válido elimina la sesión de Neon, revoca la sesión Keycloak y rechaza la cookie anterior.

## Cambios

- El fixture PowerShell reconcilia el mapper API, la URI callback de su cliente BFF y el usuario sintético no verificado en su realm local de pruebas. Mantiene usuario/realm de ensayo; no toca las cuentas del realm de producción.
- Se añadió `cloud/portal/tests/bff.integration.mjs`, que prueba el BFF contra el servidor OIDC/Neon reales y limita su limpieza a la sesión/flujo creados por esa prueba.
- El modo de test del BFF permite HTTP sólo para `127.0.0.1:18443` cuando `NODE_ENV=test`; valida antes que issuer y endpoints de metadata apunten al fixture loopback. Producción sigue exigiendo HTTPS.
- La plantilla de Keycloak requiere contraseña de al menos 15 caracteres. El registro permanece cerrado en el realm vivo hasta que SMTP esté configurado y probado.
- Se actualizó `cloud/README.md` y `TASKS.md` con el estado real de los proveedores y los gates restantes.

## Verificación

- `powershell -NoProfile -ExecutionPolicy Bypass -File cloud/deploy/Test-KeycloakNeon.ps1`: **55 + 16 checks PASS** y `PASS realm/signing configuration survives real container restart with Neon persistence.` Exit code 0.
- `npm test` en `cloud/portal`: 5/5 PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File cloud/deploy/Build-Portal.ps1`: tests 5/5 y build Next.js 16.4 con Webpack PASS en la copia NTFS.
- API pública de staging: `/health/live` y `/health/ready` devolvieron 200. Portal y alias personalizado responden HTTP 200. `/api/auth/me` sigue 503 porque aún no hay variables de cuenta conectadas.
- Cloud Run quedó con escala de servicio mínima 0 y máxima 1 en ambos servicios; esta configuración no es un corte duro de gasto.

## Seguridad y límites

El test de sesión se ejecuta sólo en el realm sintético y no usa una cuenta real ni envía correos. El BFF no comparte tokens con React. El login no autoriza vincular dispositivos ni emitir bloqueos. Los secretos permanecen en Secret Manager y vaults DPAPI; la prueba no imprime valores.

Los mapeos `auth.focusguard.totemmassmedia.com` y `api.focusguard.totemmassmedia.com` existen en estado `PermissionDenied`, porque la cuenta `transcitynorwalk@gmail.com` no está verificada como propietaria del dominio en Search Console. No se agregó esa cuenta ni se editaron los DNS de esos hosts. Se pidió confirmación específica al propietario. La app pública queda sin login mientras falte DNS/HTTPS y configuración BFF.

Resend tiene DNS de remitente verificado y la clave SMTP está almacenada de forma protegida, pero el SMTP de realm todavía no está configurado. No se probó alta, verificación o recuperación por email. No hay Zero Data Retention global: identidad y sesiones necesarias permanecen en Neon/Keycloak; Vercel y Resend siguen siendo procesadores.

## Siguientes gates

1. Tras autorización del dominio, agregar la cuenta GCloud como propietaria verificada, esperar que Cloud Run exponga sus DNS records, guardarlos en cPanel y verificar certificados.
2. Configurar SMTP de Keycloak desde Secret Manager, probar entrega con una cuenta de ensayo autorizada y sólo entonces habilitar registro.
3. Cargar variables sensibles del BFF en Vercel y probar login, perfil/seguridad y logout por HTTPS.
4. Integrar el cliente nativo al Host/React con restauración/refresh DPAPI y recuperación ante logout/revocación; después generar y validar el instalador distribuido.
