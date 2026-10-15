# Host — plan de sesión automática de ajustes

## Alcance

Crear una sesión Host-owned que componga material de device vault, MembershipChain, transporte autenticado, stores locales protegidos, initializer, worker, proyección y runtime. Su construcción no inicia solicitudes cloud; la primera inicialización debe requerir confirmación explícita.

## Aceptación

1. Exigir sender ligado al mismo AccountBinding y device vault vigente con chain completa, rol WriteSettings y sesión de transporte.
2. Usar un EntityId estable, distinto al documento de transferencia única.
3. No crear outbox ni enviar nada sin `ownerConfirmed`; guardar consentimiento solo tras un initializer/reconcile exitoso.
4. Mantener material/crypto vivo durante el runtime y disponerlos al cerrar.
5. Exponer local edit/conflict coordinators para la futura UI.
6. Probar construcción inerte, rechazo de opt-in sin confirmación y creación cifrada de baseline con confirmación.

## Límites

Este checkpoint prepara la composición; aún no agrega acciones de bridge/UI ni lo conecta a `HostAccountService`. No se accede a staging ni se instala.

## Resultado

Implementado el 14 de octubre. Los seis criterios se cubren en `HostSettingsSyncSessionTests` y la suite Host pasa 224/224. La suite Sync pasa 139/139. El candidato `.163` pasó `scripts/test-installer.ps1` y `--package-self-test`; sigue sin instalarse. Detalle, hashes y límites en [checkpoint de implementación](20261014-host-settings-sync-session.md).
