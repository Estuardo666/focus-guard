# Fase 2: prueba en dos PC Windows

Aceptación física principal completada el 14 de octubre de 2026. El propietario confirmó las dos direcciones: hermano→propietario para tema/idioma y propietario→hermano para tema/idioma/duración. En la PC del hermano, sesión de cuenta y ajustes permanecieron tras reiniciar y cerrar/reabrir FocusGuard. Reglas, perfiles y permisos locales no se resetearon; son datos propios de cada PC y no se sincronizan ni se transfieren en esta versión. Las capturas confirman login, vínculo y E2EE, pero no incluyen el preview de importación ni el hash del binario instalado en la PC del hermano. La captura inmediatamente posterior al update mostraba «Not connected»; el propietario inició sesión manualmente. Sin captura pre-update no se atribuye ese estado al instalador.

Los pasos de ensayo siguientes quedan como referencia reproducible; no son gates pendientes. Los únicos checks aún abiertos son los smokes complementarios que se identifican como opcionales.

Nota histórica: el propietario había informado que la PC del hermano no estaba disponible; ese estado quedó superado cuando la tuvo de vuelta y realizó la prueba en persona. La transferencia principal, la persistencia tras reiniciar/reabrir y la conservación de reglas, perfiles y permisos locales ya están aceptadas por reporte directo.

La PC del hermano estuvo fuera de disponibilidad desde el 13 de octubre; el propietario completó las verificaciones en persona cuando la recuperó. Este entorno no tuvo acceso remoto a esa PC.

El propietario confirmó que la PC del hermano importó correctamente tema, idioma y duración; esto acepta que abrió y descifró el documento. También confirmó que sesión y ajustes persistieron al reiniciar y cerrar/reabrir, y que las reglas, perfiles y permisos de esa PC no se resetearon. No se afirma que esos datos locales se hayan sincronizado: siguen separados por PC. La vinculación física del Samsung está aceptada y no fue necesario volver a vincularlo.

La compilación de ensayo está en `artifacts/installer/FocusGuard-0.1.166-Setup.exe` (SHA-256 `B20DD3142F020B0A61643D672FC1F2F1DD2A587CA42FE89E38D4465C49F572DA`). Pasó `scripts/test-installer.ps1` y `--package-self-test`. En la PC del propietario, `FocusGuard.Client.dll` coincide con el hash de producción .166 (`8E298EC6A92F7BA9A739D9FFC60789CC33395EA95114754C1A8E5201ABBCAF28`), el servicio está Running y `--service-health-check` terminó con código 0; la captura muestra la sesión iniciada y el equipo vinculado. En la PC del hermano, el propietario reporta .166 instalada; confirmar versión y vínculo, sin volver a instalar salvo que la versión observada sea distinta ni volver a emparejar dispositivos aprobados.

Consulta posterior de sólo metadatos: Android está Consumed/Active; la única solicitud Windows registrada sigue Pending pero su plazo terminó el 13 de octubre a las 00:27:52 UTC, antes del ensayo actual. No hay una nueva solicitud Windows en esa instantánea. Este registro histórico no demuestra fallo ni progreso de la PC del hermano; falta conocer el paso visible en ese equipo.

## Preparación del ensayo (referencia)

- Usar dos PC distintos, con acceso del propietario al equipo del hermano.
- Paquete candidato para el próximo ensayo: `D:/Users/Stuart/Documents/Portafolio/Trabajos/Totem/FocusGuard/artifacts/installer/FocusGuard-0.1.166-Setup.exe`.
- SHA-256: `B20DD3142F020B0A61643D672FC1F2F1DD2A587CA42FE89E38D4465C49F572DA`.
- En ambas PCs, comprobar antes que no haya una sesión activa ni preferencias sin guardar; anotar versión, correo, estado «vinculado», solicitudes pendientes, tema, idioma, duración, perfiles, reglas y permisos.
- Conservar los datos y el kit del Windows ya vinculado. No cerrar sesión, desvincular, revocar miembros, restablecer contraseñas ni mostrar/copiar el kit de recuperación.
- La transferencia no cambia sesiones. El ensayo principal no inicia un bloqueo; el smoke local de sesión descrito abajo se hace al final y es independiente.

## Vincular el segundo Windows (registro del flujo)

1. La PC del propietario ya tiene la .166 y el propietario reporta que también la instaló en la del hermano. En la PC del hermano, confirmar que la versión es .166 y que la cuenta muestra correo verificado y «Vinculado a esta cuenta». Tras el update hizo falta iniciar sesión; no volvió a solicitar aprobación ni creó una solicitud pendiente. No desinstalar ni limpiar datos.
2. Si un equipo aparece desvinculado, solicitar aprobación sólo entonces. Comparar código y todos los caracteres de la huella en ambos equipos, en persona; aprobar en el diálogo nativo sólo si coinciden. No compartir contraseñas, códigos MFA ni kit de recuperación.

## Demostrar transferencia y descifrado (procedimiento aceptado)

1. En ambos PCs, anotar los valores iniciales de tema, idioma y duración, más una referencia de reglas de apps/sitios, perfiles, permisos y sesión local. No iniciar ni bloquear una sesión. **Completado según reporte directo del propietario; se conserva como procedimiento reproducible.**
2. Usar la PC del propietario como origen y la del hermano como receptor. Si tema, idioma y duración ya coinciden, cambiar temporalmente sólo esos tres valores del origen y anotar los anteriores para restaurarlos después.
3. En el origen, pulsar «Compartir ajustes seleccionados». Marcar Tema, Idioma y Duración predeterminada y confirmar.
4. En la PC del hermano, pulsar «Importar ajustes seleccionados». Antes de confirmar, comprobar que la vista previa contiene exactamente los tres valores del propietario. Verlos en este receptor demuestra que abrió el documento cifrado.
5. Confirmar la importación, esperar el mensaje de guardado y reiniciar la UI si lo pide. Verificar que tema, idioma y duración quedaron aplicados también después de reabrir FocusGuard.
6. Comprobar que reglas de apps/sitios, perfiles, permisos, enforcement y sesión local conservan sus valores anteriores; la importación no debe habilitar bloqueo ni cambiar una sesión.
7. Restaurar localmente los valores originales anotados en ambos equipos. No hacer esta restauración mediante otra transferencia.

## Pruebas complementarias opcionales con ambas PCs

La aceptación principal ya está cerrada. Estas comprobaciones son opcionales y no bloquean Fase 2; amplían la observación de la extensión, cancelación y sesión normal local:

1. **Actualización y vínculo.** Tras actualizar la PC del hermano con .166, cerrar y volver a abrir FocusGuard. En ambas PCs confirmar que aparece la misma cuenta, «Vinculado a esta cuenta», ninguna solicitud nueva y estado del servicio conectado. Si la extensión de navegador ya está instalada, observar que aparezca disponible; no instalar extensiones nuevas durante esta aceptación.
2. **Persistencia tras reiniciar.** Aceptado el 14 de octubre: el propietario informa que reinició la PC del hermano; la captura posterior muestra «Signed in», email verificado, dispositivo vinculado, E2EE y ninguna solicitud pendiente. Esto prueba que la sesión ya iniciada y la membresía local sobrevivieron al reinicio. La captura no muestra el estado posterior del servicio ni de la extensión.
3. **Importación en ambos sentidos.** Ejecutar primero propietario→hermano con tema, idioma y duración seleccionados, como arriba. Después, desde la PC del hermano, compartir solo un ajuste (por ejemplo, idioma); en la PC del propietario importar solo ese campo y comprobar que tema y duración permanecen iguales. Reabrir la app y confirmar los valores aplicados.
4. **Cancelación sin cambios.** Abrir «Compartir ajustes seleccionados» en una PC, marcar uno o más campos y cancelar el diálogo. La revisión del código confirma que cancelar retorna antes de leer o publicar el documento de ajustes. En la prueba física, comprobar que el otro equipo no ve un nuevo valor importable y que los ajustes locales no cambiaron.
5. **Aislamiento de una sesión normal local.** Al final, con los valores restaurados y ambos equipos vinculados, iniciar en la PC del hermano una sesión normal Simple Timer de 1 minuto, sin Strict. Comprobar que el panel de cuenta siga vinculado, esperar su expiración natural y confirmar «Sin sesión activa». No editar perfiles ni reglas durante este paso.

La prueba de otra cuenta, replay criptográfico, revocación, pérdida del kit y restablecimiento de contraseña ya usa identidades sintéticas en el fixture; no repetirla con la cuenta personal ni arriesgar las membresías existentes.

## Registro de aceptación

| Comprobación | Evidencia disponible / límite |
|---|---|
| Instalación .166 en la PC del propietario | Aceptado el 14 de octubre: hash de Client coincide exactamente con .166; servicio saludable (código 0); captura muestra sesión iniciada, cuenta verificada y dispositivo vinculado |
| Instalación .166 en la PC del hermano | Aceptada por declaración del propietario y capturas del 14 de octubre: instaló 1.0.166; después de iniciar sesión, el correo aparece verificado y el dispositivo sigue vinculado con E2EE; no hay solicitud pendiente. No se obtuvo hash del binario instalado en esa PC |
| Inicio de sesión después del update en PC hermano | Aceptado como estado observado: la primera captura posterior al update mostraba «Not connected»; el propietario inició sesión manualmente y luego confirmó el vínculo. Sin evidencia previa al update no se atribuye el estado de logout al instalador |
| Persistencia tras reiniciar la PC del hermano | Aceptada el 14 de octubre: después del reinicio, captura muestra Signed in, email Verified, Device Linked, E2EE y sin solicitudes pendientes |
| Estado de extensión en PC hermano | Pendiente: la primera captura mostraba el browser extension «Disconnected» antes del login; la captura posterior no permite confirmar su estado tras iniciar sesión |
| Dos Windows físicos y misma cuenta | Aceptado en el flujo anterior: fotos/contexto muestran PC propia y la de su hermano con la misma cuenta. Versión de esa prueba no visible |
| Aprobación y segundo Windows vinculado | Aceptado en el flujo anterior; la captura posterior al login vuelve a confirmar el vínculo, E2EE y ninguna solicitud pendiente; no fue necesario volver a aprobar |
| Documento abierto en el segundo Windows | Aceptado por reporte directo del propietario: compartió desde su PC y confirmó importación correcta de tema, idioma y duración en la PC del hermano. No hay captura del preview/valores |
| Tema e idioma importados | Aceptado en ambos sentidos: hermano→propietario y propietario→hermano, según reporte del propietario |
| Duración predeterminada importada y persistida | Aceptada propietario→hermano según reporte directo tras reabrir FocusGuard |
| Tres ajustes persisten tras reabrir FocusGuard | Aceptado por reporte directo del propietario |
| Reglas, perfiles y permisos locales no se resetearon | Aceptado por reporte directo en la PC del hermano. Estos campos permanecen locales y no se sincronizan entre PCs en la versión actual |
| Sesión de cuenta persiste tras reiniciar y reabrir | Aceptado por reporte directo del propietario; la captura tras reiniciar confirma sesión iniciada y dispositivo vinculado |
| Sesión local de enfoque activa durante la transferencia | No probada físicamente; es un smoke opcional independiente. El usuario confirma persistencia de la sesión de cuenta, no que hubiera un temporizador activo durante la importación |
| Sentido propietario→hermano | Aceptado por reporte directo: el propietario confirma que las tres opciones se importaron correctamente en la PC del hermano; conservar como límite que no se adjuntó captura del preview |

Evidencia: adjuntos `B317733D-2036-4CC0-AF7D-F8B4DD0C8DE1`, `08F81712-2DC8-46CB-A5A5-D7EC96EF0281` y `4AD651A2-2DC9-4C57-B0F3-77F993BACE45`, además de los reportes directos del propietario. Se acepta por reporte la importación de las tres opciones propietario→hermano, la persistencia tras reiniciar/cerrar/reabrir y la conservación de datos locales. No hay captura del preview ni hash del binario del hermano. No atribuir el estado «Not connected» al instalador sin baseline pre-update. La transferencia es manual; la sincronización automática es el objetivo de Fase 3.

La aceptación principal de Fase 2 está completada por la prueba física y los reportes directos descritos arriba; el vínculo por sí solo no fue usado como evidencia de descifrado. Aislamiento de otra cuenta, consumo único/replay, revocación y contraseña independiente de E2EE tienen evidencia sintética de integración (**125 checks**, revalidada el 14-oct); no se deben repetir alterando credenciales personales para esta prueba física.

Las pruebas físicas complementarias de cancelación, estado de extensión y una sesión activa de enfoque no se ejecutaron y permanecen opcionales. La limpieza de artefactos antiguos está registrada por separado en `TASKS.md`; un rechazo de política impidió borrar la salida .164, por lo que ese objetivo operativo no se marca como realizado.
