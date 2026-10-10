# FG-217 — Edición de bloqueos de descanso desde Profile React

## Resultado

El editor Profile ahora tiene una divulgación de resumen de ancho completo que explica el efecto de las excepciones: las reglas normales se pausan durante el descanso y los destinos listados siguen bloqueados. Dentro se pueden consultar, paginar, agregar y quitar bloqueos de apps y sitios; el selector sólo ofrece tipos de coincidencia y no una acción `Allow`. Las acciones están deshabilitadas cuando la conexión, el piloto o los permisos no permiten editar.

El Host devuelve páginas acotadas de seis reglas y una revisión independiente de `ProfileCycleSettings`. Los comandos de escritura son tipados, requieren usuario autorizado, servicio inactivo, documento actual y operationId consumible. SQLite compara y reemplaza las reglas en una transacción inmediata; Classic usa el mismo CAS y recarga los valores vigentes cuando encuentra un conflicto. Cambiar una regla conserva el preset predeterminado y las reglas del otro grupo. Ninguna regla existente se convierte ni se elimina automáticamente.

El marco visual sigue graphite/mint/Geist. UI/UX Pro Max llevó a copy visible, etiquetas y foco de teclado; Emil/Apple orientaron la divulgación nativa sin animación; Front-end Design mantuvo la identidad existente. Taste excluye dashboards y flujos multipaso, así que sólo se aplicó como disciplina anti-decoración y de copy específica.

## Verificación

- React: 16 archivos, 80 pruebas aprobadas; valida páginas, revisiones separadas, Block-only y que no haya retry tras conflicto.
- Host: 140 pruebas aprobadas; pipe real + SQLite lee/añade/quita excepciones y rechaza escrituras con una revisión obsoleta.
- Storage: 38 pruebas aprobadas; dos clientes SQLite muestran compare-and-swap atómico y preservación de la escritura ganadora.
- Client: 109 pruebas aprobadas.
- Desktop: 3 regresiones enfocadas de lectura/guardado CAS y edición de excepciones aprobadas.
- Build Release de la solución: 0 warnings, 0 errores; TypeScript, Vite, CSP y chunks locales aprobados.
- `graphify update .` — 9,998 nodos, 25,415 aristas, 979 comunidades.
- Package 0.1.66: 29/29 recursos del manifiesto, 0 ausentes y 1.730 entradas ZIP. SHA-256 igual en las tres copias del Setup (`8288180A6BD707406256539057C2C61AC780B30E796270D0A9DBCF25D1688F80`); menú ZIP `252DA382639A669697639943C63112B6F96EDAC64F3E97747614F9E418735BC8`. No instalado.

## Pendiente

La revisión visual del Host instalado y la prueba en Windows con DPI real siguen pendientes. Se intentó avanzar la instalación autenticada de 0.1.66, pero el PowerShell de esta sesión no tiene token elevado; el instalador rechazó la llamada en su guard de elevación antes de modificar sistema o datos. El diagnóstico de sólo lectura previo mostró servicio y extensión listos y sesión inactiva.
