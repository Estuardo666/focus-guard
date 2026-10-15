# Fase 2 — plan del checkpoint de transferencia de ajustes

## Objetivo

Permitir que el usuario comparta una vez una selección explícita de ajustes de presentación con otro dispositivo vinculado y que el receptor elija qué importar. La carga útil viajará como documento E2EE firmado y versionado por CAS; sin sincronización continua en este checkpoint.

## Alcance permitido

- Tema global (`Dark`/`Light`).
- Idioma de interfaz (`es`/`en`).
- Duración predeterminada del temporizador (`1–720` minutos).

Excluir reglas de aplicaciones/sitios, perfiles, permisos, sesiones activas, agenda, historial, notas, datos de identidad y secretos. Ambos diálogos nativos empiezan con todas las casillas desmarcadas. La importación preserva el resto de preferencias locales y advierte que el tema/idioma pueden requerir reiniciar FocusGuard.

## Diseño y seguridad

1. Compartir sólo desde un miembro activo `WriteSettings`; validar el vault y la membresía exacta.
2. Leer y verificar el documento actual (si existe), combinar únicamente los campos seleccionados y guardar la siguiente revisión CAS. Un conflicto no se sobreescribe silenciosamente.
3. Cifrar con `ContentDocumentCodec` usando la clave del epoch actual; firmar con la identidad del dispositivo. API recibe ciphertext y metadatos, nunca valores en claro.
4. Importar sólo tras verificar membresía/autor, firma Ed25519, contexto, AEAD y esquema allowlist; después abrir un diálogo nativo de selección.
5. Aplicar sólo campos marcados mediante `DesktopSettingsStore`; leer de nuevo para comprobar que persistieron.

## Validación requerida

Unit tests de esquema/allowlist/merge, payload cifrado entre identidades, rechazo de campo no permitido, revisión CAS, no sobrescritura ante conflicto, bridge exacto sin secretos y build/test Host+React. La integración Keycloak/Neon debe comprobar que las preferencias sólo aparecen en ciphertext, que otro usuario no puede leerlas y que el receptor elige de forma independiente los campos aplicados. La aceptación real sigue exigiendo dos PC Windows.

## Resultado del checkpoint — 2026-10-12

Implementados `SharedDesktopSettings` (allowlist/versionado, merge y aplicación sólo de campos elegidos), canonicalización JCS y diálogo WinForms para compartir/importar. `HostAccountService` exige miembro `Read`/`WriteSettings`, comprueba la membresía/autor/firma/AEAD, lee/escribe el documento `preferences.initial` con XChaCha, Ed25519 y CAS, y verifica la persistencia local al importar. El puente React envía sólo la intención; nunca transporta los valores.

Validación: Host 212/212, React 235/235, dispositivos 14/14, vectores C#/Node 36/36, Keycloak/Neon/DPAPI 104 checks (incluye preferencias en ciphertext, pairing, aislamiento, CAS, replay y revocación), staging TNC 55 checks. Setup autónomo 0.1.152, ZIP y paquetes Chrome/Edge pasaron `scripts/test-installer.ps1`, `--package-self-test`, paridad y lectura de ZIP. No instalado.

Setup SHA-256 `1E2AF05B5CDEA1CE38F29A36D2A712388CAD7F08B24DAD488A50A0B09D78DECC`; ZIP SHA-256 `8DFCD6105A212FAF92D4C337B2B47C87AB97B2DBDE600F60BFCD952D9A54CACD`.

El volumen E: es exFAT y durante la última generación `Compress-Archive` reportó que el handle del ZIP se había invalidado. Se creó el ZIP en NTFS temporal, se copió al artefacto .152 y se comprobó hash idéntico, manifiesto/versión y 1.652 entradas. Setup self-test y `scripts/test-installer.ps1` pasaron con ese paquete.

La transferencia es de una sola vez y no sincroniza continuamente. La API TNC está en `tnc-api-staging-00014-bn8` (`sha256:fb2d077dd18db1889b88f8ef82bac9a5eeddf72cf27b7da15f79b8dae8877fe6`). El paquete no se instaló ni se vinculó la cuenta personal. Siguen pendientes la prueba física en dos PC, el reset real de contraseña, QR en la UI y vault/interoperabilidad Android; Fase 2 sigue abierta.
