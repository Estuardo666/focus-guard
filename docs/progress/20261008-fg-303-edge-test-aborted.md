# FG-303 — Prueba Edge aislada abortada ante inicio de sesión automático

Fecha: 2026-10-08
Estado: Edge no aceptado para bloqueo; perfil de prueba retirado.

## Hecho observado
Antes de iniciar Edge no había procesos msedge.exe. Abrí Edge con un user-data-dir nuevo en D:\TEMP\focusguard-edge-qa-20261008 y cargué edge://extensions como destino inicial; no se abrió ningún sitio ni se cargó la extensión de FocusGuard. El árbol UIA mostró el aviso “We are now syncing your browsing data across all your devices” y que Windows había iniciado sesión en Edge automáticamente. No seleccioné “Got it”, no navegué y no acepté permisos adicionales.

## Contención y estado del producto
Cerré la ventana Edge del perfil de prueba. Luego comprobé que no quedaran procesos msedge.exe con ese user-data-dir y borré exclusivamente D:\TEMP\focusguard-edge-qa-20261008, dentro de D:\TEMP; se eliminaron 434.294.538 bytes y el directorio ya no existe. No se tocó el perfil Edge personal ni se añadió la extensión. FocusGuard seguía ServiceReady/ExtensionReady, Inactive y sin SessionId; no se cambió ningún perfil.

El aviso indica una posible solicitud de sincronización automática; no puedo probar si llegó a completarse en los servicios remotos. No afirmo que Edge haya bloqueado tráfico.

## Resultado y siguiente paso
El bloqueo web real en Edge sigue pendiente. No repetiré la ruta de perfil nuevo que asoció la cuenta de Windows. Se necesita un contexto Edge que permanezca desconectado, o una alternativa de prueba owner-approved. Evidencia de esta comprobación: comando/proceso transitorio y limpieza exacta; no se conservaron datos del perfil de prueba.
