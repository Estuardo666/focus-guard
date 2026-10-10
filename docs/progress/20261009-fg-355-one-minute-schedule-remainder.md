# FG-355 — Horarios de 1 minuto con tiempo restante

## Decisión y causa

El propietario eligió mantener los horarios de 1 minuto. El editor React todavía exigía 2 min y el worker marcaba `Missed` cualquier ocurrencia a la que le quedara menos de un minuto, aunque el horario siguiera dentro de su intervalo válido.

## Cambio

- El editor y el contrato conservan el mínimo de 1 minuto. Las sesiones manuales mantienen su mínimo actual de 60 s.
- Si el worker despierta tarde, inicia una sesión programada limitada por el `EndUtc` original. No desplaza el final del horario; la sesión puede durar menos de un minuto.
- Con menos de 1 s restante, la ocurrencia se marca `Missed` sin iniciar sesión.
- Los resúmenes admiten esas sesiones programadas subminuto y la interfaz muestra «Menos de 1 min» / “Less than 1 min” en EN/ES.

## Verificación

- Cuatro pruebas cubren un horario de 1 min que inicia 5 s tarde y termina exactamente en su fin programado, una ocurrencia sin tiempo, menos de 1 s restante y la validación/presentación de un resumen subminuto.
- `dotnet test FocusGuard.slnx --configuration Release --no-restore`: 885/885.
- `npm test`: 136/136; `npm run build` y los contratos de distribución UI pasan.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.124`: pasa para Chrome y Edge.
- `scripts/test-installer.ps1`: pasa.
- Cinco smokes del Host .124 (`--smoke`, runtime faltante, renderer failure, renderer crash y window DPI): todos exit 0.
- Setup .124 SHA-256: `E20BC98A690AC7FA14F43C43E36F53B5C6A7607AA2925BA45D8309E0B401B9DC`.
- Distribución ZIP .124 SHA-256: `F5F283B5CBCFA0EE50A8E5C4B57856ACCBDB8ECE6629B3603DB1C9544B26D261`.

## Estado de instalación

.123 sigue instalado. .124 está listo como candidato, pero aún no se ha aceptado en el Host real; por tanto, las pruebas demuestran el contrato y el paquete, no el comportamiento en el servicio instalado. La instalación requiere el flujo elevado normal del Setup, y el proceso actual de Codex no tiene token elevado. La extensión local de Edge sigue en .98. No se cambió configuración del usuario ni se instaló el candidato.

## Siguiente paso

Instalar .124 por el flujo autorizado y verificar en el Host real un horario de 1 minuto, incluyendo arranque tardío y fin original. Después, seguir con las evidencias restantes de Fase 9: Edge actualizado, FG-354 sin host nativo previo, reboot/recovery, DPI/monitor, accesibilidad de Windows y carga sostenida. Avalonia sigue disponible y la Fase 10 continúa cerrada.
