# FG-358 — Próximo inicio y fin de cada horario

## Requisito y diseño

`UX_SPEC.md` pide que Programación muestre el próximo inicio y fin local. El servicio ya calculaba ambos extremos en `WeeklyOccurrenceCalculator`, pero el DTO solo exponía `NextStartUtc`; la tarjeta mostraba el inicio, la duración y una zona Windows, dejando que la persona dedujera el fin.

| Antes | Después | Motivo |
| --- | --- | --- |
| La tarjeta mostraba solo el próximo inicio; las fechas se formateaban en la zona local actual del equipo. | El servicio expone también el fin y la zona IANA correspondiente; React muestra ambos instantes en la zona guardada para el horario. | Se ve el intervalo real calculado, incluso si la zona configurada difiere de la zona actual del equipo. |

## Implementación y compatibilidad

- `ScheduleView` agrega `NextEndUtc` y `TimeZoneIanaId`, derivados por `DesktopWorkflowService` de la ocurrencia que ya obtiene del calculador de dominio.
- La lista revisionada acepta una bandera opcional `includeNextEndUtc`; el Host React la envía en lectura y después de cambios. La respuesta ampliada solo se emite cuando la piden.
- La bandera usa omisión por defecto y es opcional al leer requests. Clientes antiguos siguen enviando el payload existente; las respuestas sin extensión omiten los campos nulos para conservar su contrato JSON exacto.
- El puente valida UTC, `EndUtc > StartUtc` y la zona IANA antes de exponerlos. La UI admite tanto la respuesta antigua como la nueva, rechaza intervalos inválidos y deja “Fin no disponible” si el servicio antiguo devuelve un inicio sin fin.
- `Intl.DateTimeFormat` solo presenta los instantes del servicio en la zona IANA. No se porta el cálculo de recurrencia o DST al frontend; los horarios no se modifican ni se habilitan.

## Verificación

- C# contract tests cubren requests antiguos/nuevos y respuesta legacy sin campos extra; intervalos con fin antes del inicio se rechazan.
- Service test fija el reloj en viernes 09:59 UTC y verifica que el horario de 1 minuto informa inicio 10:00 y fin 10:01; el fin solo aparece en respuesta revisionada con opt-in.
- React tests cubren lista legacy, fin válido, fin inválido y zona IANA inválida; `ScheduleEditor` muestra el fin formateado en la zona del horario.
- Suite completa .NET: 888/888. React: 138/138. `npm run build` verifica TS, CSP, contraste, responsive y estilos existentes.
- Paquetes Chrome/Edge .127, pruebas del instalador y cinco smokes del Host: pasan.
- ZIP .127: 1,732 entradas; `zipfile.testzip()` no encontró errores.
- Setup SHA-256: `E66776A179D7FCC39B52F68ECA247F8F529AA335537097C642B8E23035BD4EC7`.
- ZIP SHA-256: `E98885FCF8F2B71747CD0B525575E2BB7A1A29302B2CCFD56A5F2CE278AC0038`.

## Instalación y siguiente paso

.123 sigue instalado; el Host de Program Files PID 25144 no se sustituyó. .127 está empaquetado en `D:\TEMP\FocusGuard-0.1.127-fg358-next-end` pero no se aceptó en el Host real. El primer intento de .126 dejó E casi sin espacio al comprimir; este paquete se construyó directamente en D y E quedó con aproximadamente 3.2 GB libres. Completar aceptación instalada de la zona/fin, luego continuar Fase 9 con Edge, FG-354 sin host nativo previo, reboot/recovery, DPI/monitor, accesibilidad de Windows y carga sostenida. Avalonia sigue disponible; Fase 10 no se inicia.
