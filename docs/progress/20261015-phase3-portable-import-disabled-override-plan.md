# Fase 3: importación portátil con sync revocada y override local

## Hipótesis

La ruta portátil sin consentimiento compara y escribe `DesktopSettings`, pero no actualiza una excepción DPAPI existente. Al reactivar sync, la proyección reaplica la excepción anterior y deshace el campo importado.

## Plan

1. Extender el ensayo Host de revocación para importar un archivo seleccionando un campo overridden.
2. Comprobar que se aplica localmente, el override DPAPI también cambia, el consentimiento sigue revocado y no hay transporte.
3. Reactivar explícitamente y proyectar: el valor importado debe persistir. Probar además que un campo sin override conserva la semántica local existente.
4. Ejecutar prueba focalizada, suite Host, formato, instalador `.179`, self-test, guard, test-installer y graphify.

UX/seguridad: el preview/revisión portátil sigue gobernando la selección; los valores con sync revocada se guardan solo en la PC. Las excepciones solo se escriben para campos ya declarados locales en el outbox DPAPI de la cuenta/dispositivo vinculado. No se reactiva el consentimiento ni se transmite nada.
