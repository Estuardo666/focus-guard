# Instalador — espera de disponibilidad autenticada

El intento .164 terminó con ExitCode 12 / Exchange / TaskCanceledException. Rollback restauró Client y Host .163 por SHA-256. La prueba repetida sobre esa instalación falló inicialmente; después cambió el PID del servicio, aparecieron sus tres pipes y la misma prueba autenticada devolvió Ready/0. No confundir el diagnóstico IPC de 11 de octubre que el instalador imprimió con una denegación nueva. El proceso actual y el manifiesto conservan hashes coherentes.

Implementar tres intentos acotados de la comprobación autenticada existente. Repetir únicamente resultados Exchange=12 o NotReady=14; Bootstrap=11 y resultados desconocidos fallan inmediatamente. Cada intento mantiene el proceso exacto del propietario, SID/sesión/integridad, ocho lecturas y las validaciones actuales. Sólo exit 0 permite continuar; tres fallos mantienen rollback. No cambiar ACL, roles, endpoints ni detener el servicio para diagnosticar.

Probar secuencias transitorias y errores persistentes con los cuerpos reales de las funciones PowerShell extraídos del AST. Generar .165 como hotfix de .163 con el Client corregido en .164 y el script de instalación nuevo, sin integrar los cambios Host/UI de Fase 3 en desarrollo. Probar self-test y hashes embebidos; dejar instalación física al propietario porque esta sesión no está elevada y no debe autoelevarse.
