# Fase 3: overrides locales durante conflicto normal — plan

## Brecha

El test HTTPS dual Host cubre conflicto CAS de duración sin overrides locales. El flujo normal de resolución podría no preservar tema/idioma locales de ambos dispositivos aunque las pruebas de coordinador y el reset ya lo cubren por separado.

## Entrega prevista

Extender la integración dual Host con overrides de tema/idioma distintos o excluidos, crear el conflicto compartido de duración y resolverlo por `HostAccountService`. Comprobar que el campo global elegido converge mientras cada dispositivo mantiene su presentación local; retirar después los overrides localmente sin nueva publicación, antes del reset existente.

Seguridad y recuperación: solo el campo disputado cambia en el documento E2EE; los overrides permanecen por dispositivo en DPAPI y no elevan permisos.

## Aceptación

El test HTTPS de dos Hosts prueba estado local tras rebase y retirada, transporte sin POST por los overrides, preferencias locales ajenas intactas. Ejecutar prueba focalizada y suite Host, formato y graphify. Test-only; no instalar ni desplegar.
