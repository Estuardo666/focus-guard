# FG-411 — prueba intermitente de ProcessAgent

## Diagnóstico

Al repetir `dotnet test FocusGuard.slnx --no-build` falló, solo con la suite completa en paralelo, `NamedPipeProcessAgentClientTests.BadPeerCannotHoldOrForgeAnExchange(kind: "correlation")` en la línea 55 (`Assert.True(exception is InvalidDataException or IOException)`). Aislada siempre pasaba.

Causa: el cliente de la prueba tenía 500 ms para todo el intercambio (conectar, escribir, que el peer de `Task.Run` lea y responda, y leer). Con el thread pool cargado, el peer puede no responder a tiempo y el cliente lanza `TimeoutException`, así que la clasificación esperada falla.

Reproducción determinista: un `Task.Delay(700)` temporal en el peer para `correlation` produce el mismo fallo, con `actual: TimeoutException`.

## Corrección (solo pruebas)

- Los casos en que el peer responde (`eof`, `malformed`, `correlation`) usan `NamedPipeProcessAgentClient.DefaultTimeout` (2 s, el máximo permitido y el de producción). Los casos que validan el plazo vencido (`partial`, `silent`, `nonreading`) mantienen 500 ms. El margen de espera es el plazo + 3 s y la cancelación del peer, 10 s.
- La aserción ahora informa del tipo y el mensaje de la excepción inesperada.
- Mismo patrón en `RealServiceReconnectPreservesAuthoritativeSessionAndIndependentBrowserChannel`: los ticks que deben tener éxito pasaron de 250 ms a `DefaultTimeout`. El tick con el servidor detenido sigue esperando el fallo.

No se debilita la validación: se siguen distinguiendo la respuesta con correlación incorrecta, la malformada, el EOF y los tres plazos vencidos.

## Verificación

- Con el retraso de 700 ms aplicado a `eof`, `malformed` y `correlation`, la teoría pasa 6/6 (el probe se retiró después).
- Build Debug sin warnings ni errores; ProcessAgent 26/26.
- Suite completa 909/909 en **11 de 11 corridas** consecutivas (6 tras la primera corrección y 5 tras la segunda). Antes fallaba aproximadamente en una de cada cuatro.
- Sin cambios en componentes distribuidos; no hace falta regenerar el instalador.
