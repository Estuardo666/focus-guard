# Fase 2 — plan de consumo de MembershipChain en Windows

## Alcance

El API ya devuelve una cadena firmada de membresía en `GET /v1/bootstrap/membership`. Ampliar el DTO privado de Host para conservarla y validar cadenas no vacías contra la revisión/digest DPAPI del dispositivo. Para abrir documentos de ajustes, resolver el autor mediante la membresía histórica exacta (`document.MembershipRevision`) y exigir `WriteSettings` allí.

## Aceptación

1. El Host deserializa y conserva `MembershipChain` del contrato API.
2. Las cadenas recibidas, si presentes, verifican firmas, digest final, transiciones y ancla local.
3. Leer un documento antiguo no consulta solo los miembros actuales: exige cadena completa y permiso `WriteSettings` en la revisión firmante.
4. Historia ausente/truncada o transición inválida falla cerrada antes de descifrar.
5. Ejecutar pruebas/build del Host y validar el instalador candidato; no instalar ni desplegar.

## Riesgo y límite

Esto endurece la transferencia única existente y prepara el mismo contrato para sync automático. No conecta el nuevo runtime ni cambia el documento de sync. Si un servidor antiguo no devuelve cadena, las lecturas de ajustes fallarán explícitamente en lugar de asumir que el miembro actual firmó el contenido histórico.
