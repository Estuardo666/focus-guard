# FG-373 — Consolidación de la matriz de fases

## Objetivo

Actualizar la tabla de Fase 3–9 para que refleje el estado vigente después de las aceptaciones instaladas FG-361–372 y distinga resultados comprobados de los gates aún pendientes.

## Resultado

- Fase 3 ahora incluye la pérdida/reconexión real del Agent durante Active en .123 (FG-368), con repetición en .128 y reboot/recovery aún pendientes.
- Fase 4 resume los probes instalados, la ACL/hash audit y la reconexión del Agent en .123; deja abierta la matriz restante, el ACK dedicado, Edge, rollback .128 y reboot/Strict.
- Fase 5 incorpora la verificación del Host propietario/Medium y Per-Monitor V2 en el monitor actual; transición real de monitor/DPI, Windows 11 y accesibilidad siguen pendientes.
- Fases 6–9 separan el candidato .128 validado en paquete de la aceptación que requiere instalarlo.
- Avalonia y Fase 10 permanecen sin cambio; no se modificó código ni estado de máquina.

## Validación

La tabla mantiene una fila por cada fase 0–10. Se verificó que el encabezado vigente aún indica .123 instalado y .128 candidato no instalado, coherente con las evidencias de paquete y las comprobaciones de instalación.
