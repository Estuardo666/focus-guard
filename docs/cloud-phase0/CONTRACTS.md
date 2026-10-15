# CLOUD-0 — Contratos cloud, envelopes e IPC futuro

Fecha: 2026-10-11. Versión 0.1.0-design. No endpoints implementados; no código de producto modificado.
Este documento es normativo para el diseño futuro junto con ADR 0016–0019. El JSON OpenAPI está embebido para mantener el entregable en Markdown.

## 1. Límites y compatibilidad

Máximo 10 dispositivos, 100 perfiles, páginas de 50; cuerpo comando 65536 bytes UTF-8 serializados, documento 262144 bytes serializados. Base64/firmas cuentan. No compression bomb ni payload chunking para eludir límites IPC.
Los maxLength de schemas son techos auxiliares; siempre validar tamaño total bytes antes de parsear/descifrar.

schema/protocol version exactos; unknown/duplicated fields rechazados. JSON seguro (RFC8785), UUID lowercase, base64url sin padding con tamaño decodificado exacto, UTC con ms/Z. Revisiones seguras enteras. Validar formato, lengths y constraints cruzadas aun si un validador trata format como anotación.

GET no revela recursos de otra cuenta: 404 uniforme. No loggear bodies/cookies/tokens/ciphertext. 401/403 genéricos sin enumeración. No hacer retries de consent, conflict, inválido o expired.

## 2. Semántica de API y authorization

Bootstrap autenticado OIDC + challenge proof por instalación; rutas pendientes solo su solicitud propia. Primer equipo crea membresía/generación una vez. API operativa token OIDC + sesión de dispositivo + X-FG-Proof y headers de binding según ADR0017, con active membership comprobada también en lecturas/SignalR.

Challenge devuelve accountId/generationId si existe sync; el primer alta valida los IDs propuestos en Membership y vincula issuer/sub una sola vez. El solicitante confirma pairing Approved mediante /bootstrap/pairings/{pairingId}/confirm, probando su clave y digest de membresía; sin esa confirmación no obtiene sesión operativa. Approved y Consumed no son estados intercambiables.

`expiresAtUtc` limita la aprobación de solicitudes Pending. Una aprobación firmada y comprometida antes del límite conserva su material para completar la recepción después de una respuesta perdida o incompatible: exige la misma clave solicitante, membresía/digest actuales sin cambios y miembro Active. No permite aprobar un Pending vencido, reactivar un revocado ni extender una sesión ya emitida. La sesión de transporte nueva mantiene su TTL acotado.

Leer una solicitud pendiente exige X-FG-Pending-Proof firmado por la clave propuesta del solicitante. Vincular método, ruta, pairingId, hash del access token, nonce de challenge y requestId/timestamp dentro de la ventana de replay de ADR0017. El backend obtiene la clave pública del registro pendiente; conocer pairingId y un login de la cuenta no sustituye esa prueba. Este permiso permite exclusivamente recuperar la aprobación/sobres propios, nunca listar solicitudes o datos de otros equipos. Antes de Active no se usa una sesión operativa como requisito circular.

OperationAuthorizationRequest usa resourceId: sessionId para StrictCommit, deviceId para DeviceRevocation y accountId para AccountDeletion/Recovery/Reset. intentDigest representa el digest canónico del scope completo de esa operación. La autorización retornada liga resourceId/action/digest/deviceId; no se necesita inventar una sesión para borrar una cuenta. Recovery/Reset sin miembro activo validan login reciente directamente en bootstrap, más proof de kit o generación nueva; no otorgan acceso a objetos previos.

Headers Idempotency-Key en mutaciones: mismo key/body produce resultado original; key con body distinto 409. TTL 90 d para efectos, nonce transporte 5 min. No enviar secretos en URL. Prueba firma request-target exacto, digest body/access token y binding; proxy no debe reescribir representación sin normalización especificada.

If-Match documento contiene revisión base decimal; creación usa `0`. CAS proposedRevision=baseRevision+1, de manera que revisión forma parte AAD antes de cifrar. GET responde ETag de revisión. Los headers ETag/Cache-Control deben añadirse en backend; ninguna respuesta privada se cachea públicamente.

Intent targetDeviceIds congelados. Normal mode debe ser Normal en POST /sessions; Strict en /strict/prepare, reject cross-mode. start/end determinan duración Normal 60–86400 s / Strict 60–14400 s al revisar; late consumption entre 1 s y fin según ADR0016. lateJoinSeconds Normal default 600; Strict siempre 0. No permitir fecha futura arbitraria en temporizador MVP: agenda es fase posterior.

Los schemas NormalIntent/StartNormalRequest y StrictIntent/PrepareStrictRequest imponen mode y Strict lateJoinSeconds=0, además de la validación semántica. Contexto local exige reloj/incertidumbre finitos, salud conocida y duración máxima consentida. Un exceso devuelve ConsentRequired con diagnóstico local ConsentDurationExceeded; contexto inválido mapea a InvalidRequest/ClockUncertain según causa. No publicar estos diagnósticos internos como códigos wire nuevos sin versionar.

Documento snapshot referenciado por ID/revision/digest inmutable. Retener bytes hasta fin+7 d. El destino lo descarga, verifica firma/membresía/digest, descifra y compila antes de Prepared. Mapeos/consentimientos y compiledPolicyDigest congelados en ticket. Hash lógico no expone texto y no sustituye el cifrado de documento.

Tickets solo los publica su deviceId activo que sea destino; ticket/session/preparation/revisiones/end deben coincidir. StrictCommit contiene exactamente los digests de todos los destinos, sin extras/duplicados. Autorización de operación del backend en un uso, misma acción/intención/iniciador. CAS y consumo auth/ticket dentro de transacción persistida.

StopNormal contra Strict → Conflict; cancelar no committed exige origen autorizado y estado Preparing/Ready. Recovery no se ejecuta via API: ACK RecoveryPending/Recovered solo informa decisión del motor local.

API export no descifra contenido, pagina con límites; cliente produce export legible localmente. PATCH /me solo documento de nombre cifrado; correo/contraseña/MFA via IdP.

## 3. Firmas y validación de envelopes

KeyEnvelope vigente: FG-SODIUM-BOX-1 según ADR0020. Sealed-box incluye su clave efímera dentro de ciphertext; no hay encapsulatedKey separado. Firma externa autentica autor; comparar payload JCS descifrado con cuenta/generación/epoch/recipient/membresía vigentes. Los resultados de key-envelope.test.mjs prueban el codec de ensayo, no sustituyen los gates Android/Web/almacén real de Fase2.

Firmar bytes: domain ASCII fijo + byte 0 + JCS(objeto sin su campo signature/originSignature). Dominios distintos FG-DOC-1, FG-INTENT-1, FG-STRICT-TICKET-1, FG-COMMIT-1, FG-STOP-1, FG-ACK-1, FG-MEMBERSHIP-1, FG-REQUEST-1. SHA256 digest sobre esos bytes canonicales, no sobre JSON incidental.

Origin intent firmado antes de recibir sequence. ServerReceipt vincula sequence/generation/intentDigest/start/end; no altera firma origen. ACK firma mode/executionKind, hashes, sequence y observedAt/end.
EventHint solo cursor opaco/generation/kind; no payload privado ni ejecución desde evento.

ACK proviene de enforcer, no pantalla. Strict=true es tipo aun tras Expired; calcular protección desde estado/end/snapshot/canales, no booleano Strict. Rechazar recibos fuera de pertenencia/destino/revisión; receipt repetido idempotente, transición antigua no hace retroceder estado.

SyncSnapshot paginado no permite ejecutar de su primera página parcialmente: obtener documentos/sesión/membresía requeridos, validar digest/cursor/generación coherentes y luego reconciliar. resyncRequired por cursor >90d no revive órdenes históricas. Snapshot actual no borra tombstones locales de sesión interrumpida.

## 4. IPC Windows propuesto, sin cambiar contrato actual

| Mensaje futuro | Rol Sync | Condición motor |
|---|---|---|
| SyncStatus / SyncCapabilities | lectura | scope SID/logon observado y manifest protegido |
| PrepareRemoteNormal | sí | consentimiento normal, cuenta/generación/mapeo, fin |
| PrepareRemoteStrict | sí | permiso Strict separado, recovery provisionado, salud/canales mínimos |
| CommitRemotePrepared | sí | ticket local one-use, revisiones, proof de commit vigente |
| CancelRemotePreparation | sí | solo reserva no comprometida |
| StopRemoteNormal | sí | mismo sessionId, modo normal, secuencia válida |
| ReportRemoteExecution | lectura | estado sanitizado sin recovery/hash/secretos |
| ProvisionRemoteConsent | no para Sync | acción local revisada por propietario mediante host autorizado |
| Provision/RotateRecovery | no para Sync | autorización local independiente y sin Strict vigente |

No reutilizar Desktop/WebViewHost, ni generar `RecoverySaved=true` para el bridge. El servicio decide reglas permitidas y credencial. UI no aplica bloqueo ni avances temporales.

IPC actual usa 64KiB, roll/role allowlist observados. Nuevos DTO/revisión de protocolo requieren SPEC/ADR, tests de rol, codec unknown fields, payload pequeño y pruebas instaladas. Ningún JSON recibido permite arbitrary paths/SQL/commands ni IDs de otra sesión Windows.

## 5. OpenAPI 3.1 (JSON embebido)

La especificación usa .invalid para placeholder. Authorization OIDC y deviceProof en la misma entrada significa AND; bootstrap declara excepción controlada OIDC/challenge. No publicar este contrato como si hubiera un servicio activo.

```json
{
  "openapi": "3.1.0",
  "info": {
    "title": "FocusGuard Cloud Phase 0",
    "version": "0.1.0-design",
    "description": "Contrato documental; no backend disponible. Reglas semánticas del Markdown son normativas."
  },
  "servers": [
    {
      "url": "https://api.focusguard.invalid",
      "description": "Placeholder reservado .invalid, nunca URL de producción"
    }
  ],
  "security": [
    {
      "oidc": [],
      "deviceProof": []
    }
  ],
  "paths": {
    "/v1/bootstrap/challenges": {
      "post": {
        "operationId": "createBootstrapChallenge",
        "summary": "createBootstrapChallenge",
        "parameters": [
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "201": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Challenge"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "security": [
          {
            "oidc": []
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/ChallengeRequest"
              }
            }
          }
        }
      }
    },
    "/v1/bootstrap/accounts": {
      "post": {
        "operationId": "registerFirstDevice",
        "summary": "registerFirstDevice",
        "parameters": [
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "201": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Account"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "security": [
          {
            "oidc": []
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/BootstrapRequest"
              }
            }
          }
        }
      }
    },
    "/v1/bootstrap/pairings": {
      "post": {
        "operationId": "requestPairing",
        "summary": "requestPairing",
        "responses": {
          "201": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Pairing"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "security": [
          {
            "oidc": []
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/PairingRequest"
              }
            }
          }
        }
      }
    },
    "/v1/bootstrap/pairings/{pairingId}": {
      "get": {
        "operationId": "readOwnPendingPairing",
        "summary": "readOwnPendingPairing",
        "parameters": [
          {
            "name": "X-FG-Pending-Proof",
            "in": "header",
            "required": true,
            "schema": {
              "$ref": "#/components/schemas/Signature"
            },
            "description": "Firma del solicitante sobre binding, challenge y metadatos de replay; no token de dispositivo activo"
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "$ref": "#/components/schemas/Id"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "$ref": "#/components/schemas/Timestamp"
            }
          },
          {
            "name": "pairingId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/PairingState"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "security": [
          {
            "oidc": []
          }
        ]
      }
    },
    "/v1/bootstrap/device-sessions": {
      "post": {
        "operationId": "createDeviceSession",
        "summary": "createDeviceSession",
        "parameters": [
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": { "$ref": "#/components/schemas/Id" }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": { "$ref": "#/components/schemas/Timestamp" }
          },
          {
            "name": "X-FG-Pending-Proof",
            "in": "header",
            "required": true,
            "schema": { "$ref": "#/components/schemas/Signature" },
            "description": "FG-PAIRING-CONFIRM-1 sobre el método, destino, cuerpo y contexto OIDC"
          }
        ],
        "responses": {
          "201": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/DeviceSession"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "security": [
          {
            "oidc": []
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/DeviceSessionRequest"
              }
            }
          }
        }
      }
    },
    "/v1/bootstrap/recoveries": {
      "post": {
        "operationId": "recoverMembership",
        "summary": "recoverMembership",
        "parameters": [
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Account"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "security": [
          {
            "oidc": []
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/RecoveryRequest"
              }
            }
          }
        }
      }
    },
    "/v1/bootstrap/resets": {
      "post": {
        "operationId": "resetLostGeneration",
        "summary": "resetLostGeneration",
        "parameters": [
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Account"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "security": [
          {
            "oidc": []
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/ResetRequest"
              }
            }
          }
        }
      }
    },
    "/v1/me": {
      "get": {
        "operationId": "readAccount",
        "summary": "readAccount",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Account"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        }
      },
      "patch": {
        "operationId": "updateAccountDisplayDocument",
        "summary": "updateAccountDisplayDocument",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/DocumentReceipt"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/AccountPatch"
              }
            }
          }
        }
      }
    },
    "/v1/devices": {
      "get": {
        "operationId": "listOwnDevices",
        "summary": "listOwnDevices",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/DevicePage"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        }
      }
    },
    "/v1/devices/{deviceId}/preferences": {
      "patch": {
        "operationId": "updateDeviceCoordination",
        "summary": "updateDeviceCoordination",
        "parameters": [
          {
            "name": "deviceId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Device"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/DevicePreferenceRequest"
              }
            }
          }
        }
      }
    },
    "/v1/devices/{deviceId}/revoke": {
      "post": {
        "operationId": "revokeDevice",
        "summary": "revokeDevice",
        "parameters": [
          {
            "name": "deviceId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Account"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/RevocationRequest"
              }
            }
          }
        }
      }
    },
    "/v1/pairings/{pairingId}/approve": {
      "post": {
        "operationId": "approvePairing",
        "summary": "approvePairing",
        "parameters": [
          {
            "name": "pairingId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Pairing"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/PairingApproval"
              }
            }
          }
        }
      }
    },
    "/v1/sync/snapshot": {
      "get": {
        "operationId": "readCurrentSyncSnapshot",
        "summary": "readCurrentSyncSnapshot",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "cursor",
            "in": "query",
            "required": false,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/SyncSnapshot"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        }
      }
    },
    "/v1/sync/time": {
      "get": {
        "operationId": "readServerTime",
        "summary": "readServerTime",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/TimeResponse"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        }
      }
    },
    "/v1/documents/{entityId}": {
      "get": {
        "operationId": "readEncryptedDocument",
        "summary": "readEncryptedDocument",
        "parameters": [
          {
            "name": "entityId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/CipherDocument"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        }
      },
      "put": {
        "operationId": "putEncryptedDocument",
        "summary": "putEncryptedDocument",
        "parameters": [
          {
            "name": "entityId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "If-Match",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 64
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/DocumentReceipt"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/CipherDocument"
              }
            }
          }
        }
      },
      "delete": {
        "operationId": "deleteDocumentWithTombstone",
        "summary": "deleteDocumentWithTombstone",
        "parameters": [
          {
            "name": "entityId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "If-Match",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 64
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Tombstone"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/TombstoneRequest"
              }
            }
          }
        }
      }
    },
    "/v1/sessions": {
      "post": {
        "operationId": "startNormalIntent",
        "summary": "startNormalIntent",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "202": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/SessionResult"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/StartNormalRequest"
              }
            }
          }
        }
      }
    },
    "/v1/sessions/strict/prepare": {
      "post": {
        "operationId": "prepareStrictIntent",
        "summary": "prepareStrictIntent",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "202": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/StrictPreparation"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/PrepareStrictRequest"
              }
            }
          }
        }
      }
    },
    "/v1/sessions/{sessionId}/strict/tickets": {
      "post": {
        "operationId": "publishStrictTicket",
        "summary": "publishStrictTicket",
        "parameters": [
          {
            "name": "sessionId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/StrictPreparation"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/StrictTicket"
              }
            }
          }
        }
      }
    },
    "/v1/operation-authorizations": {
      "post": {
        "operationId": "createFreshOperationAuthorization",
        "summary": "createFreshOperationAuthorization",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "201": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/OperationAuthorization"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/OperationAuthorizationRequest"
              }
            }
          }
        }
      }
    },
    "/v1/sessions/{sessionId}/strict/commit": {
      "post": {
        "operationId": "commitStrictIntent",
        "summary": "commitStrictIntent",
        "parameters": [
          {
            "name": "sessionId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "202": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/SessionResult"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/StrictCommit"
              }
            }
          }
        }
      }
    },
    "/v1/sessions/{sessionId}/strict/cancel-preparation": {
      "post": {
        "operationId": "cancelStrictPreparation",
        "summary": "cancelStrictPreparation",
        "parameters": [
          {
            "name": "sessionId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/StrictPreparation"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/CancelPreparation"
              }
            }
          }
        }
      }
    },
    "/v1/sessions/{sessionId}/stop": {
      "post": {
        "operationId": "stopNormalIntent",
        "summary": "stopNormalIntent",
        "parameters": [
          {
            "name": "sessionId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "202": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/SessionResult"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/StopNormal"
              }
            }
          }
        }
      }
    },
    "/v1/sessions/{sessionId}/receipts": {
      "post": {
        "operationId": "publishExecutionReceipt",
        "summary": "publishExecutionReceipt",
        "parameters": [
          {
            "name": "sessionId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/SessionResult"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/ExecutionReceipt"
              }
            }
          }
        }
      }
    },
    "/v1/sessions/{sessionId}": {
      "get": {
        "operationId": "readSessionAggregate",
        "summary": "readSessionAggregate",
        "parameters": [
          {
            "name": "sessionId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/SessionResult"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        }
      }
    },
    "/v1/export": {
      "post": {
        "operationId": "requestEncryptedExport",
        "summary": "requestEncryptedExport",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Export"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/ExportRequest"
              }
            }
          }
        }
      },
      "get": {
        "operationId": "pageEncryptedExport",
        "summary": "pageEncryptedExport",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "cursor",
            "in": "query",
            "required": false,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Export"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        }
      }
    },
    "/v1/account": {
      "delete": {
        "operationId": "requestAccountDeletion",
        "summary": "requestAccountDeletion",
        "parameters": [
          {
            "name": "X-FG-Device-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Generation-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "date-time",
              "pattern": "Z$",
              "maxLength": 24
            }
          },
          {
            "name": "X-FG-Device-Session",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "minLength": 1,
              "maxLength": 4096
            }
          },
          {
            "name": "Idempotency-Key",
            "in": "header",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          }
        ],
        "responses": {
          "202": {
            "description": "Resultado tipado; aceptación no implica enforcement",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/DeletionResult"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Autenticación inválida",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "Sin autorización",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "404": {
            "description": "Objeto no accesible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "409": {
            "description": "Conflicto/revisión/estado",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "413": {
            "description": "Límite bytes",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            },
            "headers": {
              "Retry-After": {
                "schema": {
                  "type": "integer",
                  "minimum": 1
                },
                "description": "Segundos hasta próximo intento"
              }
            }
          },
          "503": {
            "description": "Estado operativo indisponible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          }
        },
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/DeletionRequest"
              }
            }
          }
        }
      }
    },
    "/v1/bootstrap/membership": {
      "get": {
        "operationId": "getCurrentMembership",
        "summary": "Validar sesión y recuperar el snapshot E2EE vigente del propio dispositivo",
        "security": [ { "oidc": [] } ],
        "parameters": [
          { "name": "X-FG-Device-Id", "in": "header", "required": true, "schema": { "$ref": "#/components/schemas/Id" } },
          { "name": "X-FG-Device-Session", "in": "header", "required": true, "schema": { "$ref": "#/components/schemas/DeviceSessionSecret" } },
          { "name": "X-FG-Request-Id", "in": "header", "required": true, "schema": { "$ref": "#/components/schemas/Id" } },
          { "name": "X-FG-Timestamp", "in": "header", "required": true, "schema": { "$ref": "#/components/schemas/Timestamp" } },
          { "name": "X-FG-Proof", "in": "header", "required": true, "schema": { "$ref": "#/components/schemas/Signature" }, "description": "FG-REQUEST-1 firmado por la identidad de este miembro activo" }
        ],
        "responses": {
          "200": { "description": "Snapshot firmado y cadena hasta genesis", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/MembershipSnapshot" } } } },
          "401": { "description": "Access token inválido", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "403": { "description": "Proof o sesión inválidos", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "404": { "description": "Membresía no disponible", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "409": { "description": "La sesión requiere una membresía vigente; usar la prueba de solicitud propia para recuperar la cadena antes de renovar", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "429": { "description": "Rate limit", "headers": { "Retry-After": { "schema": { "type": "integer", "minimum": 1 } } }, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "503": { "description": "Servicio de membresía indisponible", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } }
        }
      }
    },
    "/v1/bootstrap/sessions/renew": {
      "post": {
        "operationId": "renewDeviceSession",
        "summary": "Renovar una sesión operativa tras verificar el estado local del dispositivo",
        "security": [ { "oidc": [] } ],
        "parameters": [
          { "name": "X-FG-Request-Id", "in": "header", "required": true, "schema": { "$ref": "#/components/schemas/Id" } },
          { "name": "X-FG-Timestamp", "in": "header", "required": true, "schema": { "$ref": "#/components/schemas/Timestamp" } },
          { "name": "X-FG-Proof", "in": "header", "required": true, "schema": { "$ref": "#/components/schemas/Signature" }, "description": "FG-DEVICE-SESSION-RENEW-1 sobre el método, ruta, cuerpo y token OIDC" }
        ],
        "requestBody": {
          "required": true,
          "content": { "application/json": { "schema": { "$ref": "#/components/schemas/RenewDeviceSessionRequest" } } }
        },
        "responses": {
          "200": { "description": "Sesión válida y snapshot E2EE vigente", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/RenewDeviceSessionResult" } } } },
          "400": { "description": "Contrato inválido", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "401": { "description": "Access token inválido", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "403": { "description": "Proof o miembro no autorizado", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "404": { "description": "Membresía no disponible", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "409": { "description": "Digest de membresía obsoleto", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "429": { "description": "Rate limit", "headers": { "Retry-After": { "schema": { "type": "integer", "minimum": 1 } } }, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } },
          "503": { "description": "Servicio de sesión indisponible", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Error" } } } }
        }
      }
    },
    "/v1/bootstrap/pairings/{pairingId}/confirm": {
      "post": {
        "operationId": "confirmApprovedPairing",
        "summary": "Confirmar posesión/descifrado del solicitante antes de Active",
        "security": [
          {
            "oidc": []
          }
        ],
        "parameters": [
          {
            "name": "pairingId",
            "in": "path",
            "required": true,
            "schema": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            }
          },
          {
            "name": "X-FG-Request-Id",
            "in": "header",
            "required": true,
            "schema": { "$ref": "#/components/schemas/Id" }
          },
          {
            "name": "X-FG-Timestamp",
            "in": "header",
            "required": true,
            "schema": { "$ref": "#/components/schemas/Timestamp" }
          },
          {
            "name": "X-FG-Pending-Proof",
            "in": "header",
            "required": true,
            "schema": { "$ref": "#/components/schemas/Signature" },
            "description": "FG-PAIRING-CONFIRM-1 sobre el método, destino, cuerpo y contexto OIDC"
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/PairingConfirmation"
              }
            }
          }
        },
        "responses": {
          "200": {
            "description": "Solicitante pasa a Active; luego obtiene device session",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/PairingConfirmResult"
                }
              }
            }
          },
          "400": {
            "description": "Contrato inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "401": {
            "description": "Login/proof inválido",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "403": {
            "description": "La firma o la membresía del solicitante no se acepta",
            "content": {
              "application/json": {
                "schema": { "$ref": "#/components/schemas/Error" }
              }
            }
          },
          "404": {
            "description": "Solicitud no accesible",
            "content": {
              "application/json": {
                "schema": { "$ref": "#/components/schemas/Error" }
              }
            }
          },
          "409": {
            "description": "Solicitud no Approved/Consumed o membresía actual incompatible",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/Error"
                }
              }
            }
          },
          "429": {
            "description": "Rate limit",
            "headers": { "Retry-After": { "schema": { "type": "integer", "minimum": 1 } } },
            "content": {
              "application/json": {
                "schema": { "$ref": "#/components/schemas/Error" }
              }
            }
          },
          "503": {
            "description": "Servicio de membresía indisponible",
            "content": {
              "application/json": {
                "schema": { "$ref": "#/components/schemas/Error" }
              }
            }
          }
        }
      }
    }
  },
  "components": {
    "securitySchemes": {
      "oidc": {
        "type": "http",
        "scheme": "bearer",
        "bearerFormat": "OIDC access token"
      },
      "deviceProof": {
        "type": "apiKey",
        "in": "header",
        "name": "X-FG-Proof",
        "description": "Prueba de posesión firmada según ADR0017; también exige sesión operativa y headers de binding."
      }
    },
    "schemas": {
      "Id": {
        "type": "string",
        "format": "uuid",
        "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
      },
      "Timestamp": {
        "type": "string",
        "format": "date-time",
        "pattern": "Z$",
        "maxLength": 24
      },
      "Digest": {
        "type": "string",
        "pattern": "^[A-Za-z0-9_-]+$",
        "minLength": 43,
        "maxLength": 43
      },
      "PublicKey": {
        "type": "string",
        "pattern": "^[A-Za-z0-9_-]+$",
        "minLength": 43,
        "maxLength": 43
      },
      "Signature": {
        "type": "string",
        "pattern": "^[A-Za-z0-9_-]+$",
        "minLength": 86,
        "maxLength": 86
      },
      "Revision": {
        "type": "integer",
        "minimum": 0,
        "maximum": 9007199254740991
      },
      "Mode": {
        "type": "string",
        "enum": [
          "Normal",
          "Strict"
        ]
      },
      "Error": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "code",
          "correlationId",
          "retryable"
        ],
        "properties": {
          "code": {
            "type": "string",
            "enum": [
              "Unauthorized",
              "Forbidden",
              "NotFound",
              "Conflict",
              "ConsentRequired",
              "RecoveryNotReady",
              "PolicyUnsupported",
              "SessionAlreadyActive",
              "SessionExpired",
              "PreparationExpired",
              "DeviceRevoked",
              "VersionUnsupported",
              "KeyEpochUnknown",
              "ClockUncertain",
              "RateLimited",
              "InvalidRequest",
              "PayloadTooLarge"
            ]
          },
          "correlationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "retryable": {
            "type": "boolean"
          },
          "retryAfterSeconds": {
            "type": "integer",
            "minimum": 0,
            "maximum": 86400
          }
        }
      },
      "ChallengeRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "purpose",
          "deviceId",
          "signingPublicKey",
          "encryptionPublicKey"
        ],
        "properties": {
          "purpose": {
            "type": "string",
            "enum": [
              "FirstDevice",
              "Pairing",
              "DeviceSession",
              "Recovery",
              "Reset"
            ]
          },
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "signingPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "encryptionPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          }
        }
      },
      "Challenge": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "challengeId",
          "nonce",
          "expiresAtUtc"
        ],
        "properties": {
          "challengeId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "nonce": {
            "type": "string",
            "pattern": "^[A-Za-z0-9_-]+$",
            "minLength": 43,
            "maxLength": 43
          },
          "expiresAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          }
        }
      },
      "Capabilities": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "platform",
          "clientVersion",
          "normal",
          "strict",
          "channels",
          "strictPersistenceVerified",
          "recoveryReady"
        ],
        "properties": {
          "platform": {
            "type": "string",
            "enum": [
              "Windows",
              "Linux",
              "Android",
              "macOS",
              "iOS"
            ]
          },
          "clientVersion": {
            "type": "string",
            "minLength": 1,
            "maxLength": 32
          },
          "normal": {
            "type": "string",
            "enum": [
              "Real",
              "Limited",
              "Simulated",
              "Unsupported"
            ]
          },
          "strict": {
            "type": "string",
            "enum": [
              "Real",
              "Limited",
              "Simulated",
              "Unsupported"
            ]
          },
          "channels": {
            "type": "array",
            "items": {
              "type": "string",
              "enum": [
                "Applications",
                "Websites"
              ]
            },
            "minItems": 0,
            "maxItems": 2,
            "uniqueItems": true
          },
          "strictPersistenceVerified": {
            "type": "boolean"
          },
          "recoveryReady": {
            "type": "boolean"
          }
        }
      },
      "Device": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "deviceId",
          "status",
          "signingPublicKey",
          "encryptionPublicKey",
          "capabilities"
        ],
        "properties": {
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "status": {
            "type": "string",
            "enum": [
              "Pending",
              "Active",
              "Paused",
              "Revoked"
            ]
          },
          "signingPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "encryptionPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "capabilities": {
            "$ref": "#/components/schemas/Capabilities"
          },
          "lastSeenUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          }
        }
      },
      "Member": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "deviceId",
          "signingPublicKey",
          "encryptionPublicKey",
          "status",
          "roles"
        ],
        "properties": {
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "signingPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "encryptionPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "status": {
            "type": "string",
            "enum": [
              "Active",
              "Revoked"
            ]
          },
          "roles": {
            "type": "array",
            "items": {
              "type": "string",
              "enum": [
                "Read",
                "WriteSettings",
                "StartNormal",
                "StartStrict",
                "ManageDevices"
              ]
            },
            "minItems": 0,
            "maxItems": 5,
            "uniqueItems": true
          }
        }
      },
      "Membership": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "accountId",
          "generationId",
          "baseRevision",
          "proposedRevision",
          "keyEpoch",
          "previousDigest",
          "members",
          "recoverySigningPublicKey",
          "recoveryEncryptionPublicKey",
          "signerDeviceId",
          "signature"
        ],
        "properties": {
          "protocol": {
            "const": "FG-MEMBERSHIP-1"
          },
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "baseRevision": {
            "type": "integer",
            "minimum": 0,
            "maximum": 9007199254740991
          },
          "proposedRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "previousDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "members": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/Member"
            },
            "minItems": 1,
            "maxItems": 10,
            "uniqueItems": true
          },
          "recoverySigningPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "recoveryEncryptionPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "signerDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "AccountKeyPayload": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "accountId",
          "generationId",
          "keyEpoch",
          "recipientId",
          "membershipDigest",
          "contentKey"
        ],
        "properties": {
          "protocol": {
            "const": "FG-ACCOUNT-KEY-1"
          },
          "accountId": {
            "$ref": "#/components/schemas/Id"
          },
          "generationId": {
            "$ref": "#/components/schemas/Id"
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "recipientId": {
            "$ref": "#/components/schemas/Id"
          },
          "membershipDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "contentKey": {
            "type": "string",
            "pattern": "^[A-Za-z0-9_-]+$",
            "minLength": 43,
            "maxLength": 43
          }
        }
      },
      "KeyEnvelope": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "suite",
          "generationId",
          "keyEpoch",
          "recipientId",
          "recipientPublicKey",
          "membershipDigest",
          "ciphertext",
          "signerDeviceId",
          "signature"
        ],
        "properties": {
          "suite": {
            "const": "FG-SODIUM-BOX-1"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "recipientId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "recipientPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "membershipDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "ciphertext": {
            "type": "string",
            "pattern": "^[A-Za-z0-9_-]+$",
            "minLength": 64,
            "maxLength": 8192
          },
          "signerDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "BootstrapRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "challengeId",
          "deviceId",
          "capabilities",
          "membership",
          "keyEnvelopes",
          "proof"
        ],
        "properties": {
          "challengeId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "capabilities": {
            "$ref": "#/components/schemas/Capabilities"
          },
          "membership": {
            "$ref": "#/components/schemas/Membership"
          },
          "keyEnvelopes": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/KeyEnvelope"
            },
            "minItems": 1,
            "maxItems": 11,
            "uniqueItems": true
          },
          "proof": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "PairingRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "challengeId",
          "deviceId",
          "signingPublicKey",
          "encryptionPublicKey",
          "capabilities",
          "proof"
        ],
        "properties": {
          "challengeId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "signingPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "encryptionPublicKey": {
            "$ref": "#/components/schemas/PublicKey"
          },
          "capabilities": {
            "$ref": "#/components/schemas/Capabilities"
          },
          "proof": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "Pairing": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "pairingId",
          "deviceId",
          "expiresAtUtc",
          "state"
        ],
        "properties": {
          "pairingId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "expiresAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "state": {
            "type": "string",
            "enum": [
              "Pending",
              "Approved",
              "Consumed",
              "Cancelled",
              "Expired"
            ]
          },
          "membership": {
            "$ref": "#/components/schemas/Membership"
          },
          "keyEnvelopes": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/KeyEnvelope"
            },
            "minItems": 0,
            "maxItems": 11,
            "uniqueItems": true
          }
        }
      },
      "PairingApproval": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "pairingId",
          "membership",
          "keyEnvelopes"
        ],
        "properties": {
          "pairingId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "membership": {
            "$ref": "#/components/schemas/Membership"
          },
          "keyEnvelopes": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/KeyEnvelope"
            },
            "minItems": 1,
            "maxItems": 11,
            "uniqueItems": true
          }
        }
      },
      "DeviceSessionRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "challengeId",
          "deviceId",
          "proof"
        ],
        "properties": {
          "challengeId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "proof": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "DeviceSession": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "deviceId",
          "generationId",
          "deviceSessionToken",
          "expiresAtUtc"
        ],
        "properties": {
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "deviceSessionToken": {
            "type": "string",
            "minLength": 1,
            "maxLength": 4096
          },
          "expiresAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          }
        }
      },
      "Account": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "accountId",
          "generationId",
          "state",
          "identityEmailVerified",
          "membershipRevision",
          "keyEpoch"
        ],
        "properties": {
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "state": {
            "type": "string",
            "enum": [
              "Active",
              "DeletionPending"
            ]
          },
          "identityEmailVerified": {
            "type": "boolean"
          },
          "membershipRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          }
        }
      },
      "DevicePage": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "items"
        ],
        "properties": {
          "items": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/Device"
            },
            "minItems": 0,
            "maxItems": 10,
            "uniqueItems": true
          },
          "nextCursor": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          }
        }
      },
      "CipherDocument": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "accountId",
          "generationId",
          "entityId",
          "kind",
          "baseRevision",
          "proposedRevision",
          "keyEpoch",
          "authorDeviceId",
          "membershipRevision",
          "nonce",
          "ciphertext",
          "signature"
        ],
        "properties": {
          "protocol": {
            "const": "FG-DOC-1"
          },
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "entityId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "kind": {
            "type": "string",
            "enum": [
              "Profile",
              "AccountPreferences",
              "DevicePreferences",
              "SessionSnapshot",
              "CyclePreset"
            ]
          },
          "baseRevision": {
            "type": "integer",
            "minimum": 0,
            "maximum": 9007199254740991
          },
          "proposedRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "authorDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "membershipRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "nonce": {
            "type": "string",
            "pattern": "^[A-Za-z0-9_-]+$",
            "minLength": 32,
            "maxLength": 32
          },
          "ciphertext": {
            "type": "string",
            "pattern": "^[A-Za-z0-9_-]+$",
            "minLength": 22,
            "maxLength": 349526
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "DocumentReceipt": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "entityId",
          "revision",
          "digest",
          "cursor"
        ],
        "properties": {
          "entityId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "revision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "digest": {
            "$ref": "#/components/schemas/Digest"
          },
          "cursor": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          }
        }
      },
      "TombstoneRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "accountId",
          "generationId",
          "entityId",
          "baseRevision",
          "proposedRevision",
          "authorDeviceId",
          "signature"
        ],
        "properties": {
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "entityId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "baseRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "proposedRevision": {
            "type": "integer",
            "minimum": 2,
            "maximum": 9007199254740991
          },
          "authorDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "Tombstone": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "entityId",
          "revision",
          "deletedAtUtc"
        ],
        "properties": {
          "entityId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "revision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "deletedAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          }
        }
      },
      "SnapshotReference": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "entityId",
          "revision",
          "digest"
        ],
        "properties": {
          "entityId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "revision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "digest": {
            "$ref": "#/components/schemas/Digest"
          }
        }
      },
      "Intent": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "accountId",
          "generationId",
          "commandId",
          "sessionId",
          "originDeviceId",
          "targetDeviceIds",
          "membershipRevision",
          "keyEpoch",
          "issuedAtUtc",
          "startAtUtc",
          "endAtUtc",
          "mode",
          "normalLateJoinSeconds",
          "snapshot",
          "originSignature"
        ],
        "properties": {
          "protocol": {
            "const": "FG-INTENT-1"
          },
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "commandId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "originDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "targetDeviceIds": {
            "type": "array",
            "items": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            },
            "minItems": 1,
            "maxItems": 10,
            "uniqueItems": true
          },
          "membershipRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "issuedAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "startAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "endAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "mode": {
            "$ref": "#/components/schemas/Mode"
          },
          "normalLateJoinSeconds": {
            "type": "integer",
            "minimum": 0,
            "maximum": 86400
          },
          "snapshot": {
            "$ref": "#/components/schemas/SnapshotReference"
          },
          "originSignature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "StartNormalRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "intent"
        ],
        "properties": {
          "intent": {
            "$ref": "#/components/schemas/NormalIntent"
          }
        }
      },
      "SessionResult": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "sessionId",
          "sequence",
          "intentDigest",
          "state",
          "endAtUtc",
          "receipts",
          "serverSignature"
        ],
        "properties": {
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sequence": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "intentDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "state": {
            "type": "string",
            "enum": [
              "Preparing",
              "Ready",
              "Committed",
              "Active",
              "PartiallyApplied",
              "Unknown",
              "Stopped",
              "Expired",
              "Cancelled",
              "PreparationExpired"
            ]
          },
          "endAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "receipts": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/ExecutionReceipt"
            },
            "minItems": 0,
            "maxItems": 10,
            "uniqueItems": true
          },
          "serverSignature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "StrictPreparation": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "preparationId",
          "sessionId",
          "intentDigest",
          "expiresAtUtc",
          "state",
          "tickets"
        ],
        "properties": {
          "preparationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "intentDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "expiresAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "state": {
            "type": "string",
            "enum": [
              "Preparing",
              "Ready",
              "Committed",
              "Cancelled",
              "PreparationExpired"
            ]
          },
          "tickets": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/StrictTicket"
            },
            "minItems": 0,
            "maxItems": 10,
            "uniqueItems": true
          }
        }
      },
      "StrictTicket": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "accountId",
          "generationId",
          "preparationId",
          "sessionId",
          "deviceId",
          "intentDigest",
          "logicalSnapshotDigest",
          "compiledPolicyDigest",
          "consentRevision",
          "mappingRevision",
          "membershipRevision",
          "keyEpoch",
          "requestedMode",
          "executionKind",
          "minimumChannels",
          "recoveryReady",
          "startAtUtc",
          "endAtUtc",
          "expiresAtUtc",
          "signature"
        ],
        "properties": {
          "protocol": {
            "const": "FG-STRICT-TICKET-1"
          },
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "preparationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "intentDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "logicalSnapshotDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "compiledPolicyDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "consentRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "mappingRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "membershipRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "requestedMode": {
            "const": "Strict"
          },
          "executionKind": {
            "type": "string",
            "enum": [
              "Real",
              "Limited",
              "Simulated"
            ]
          },
          "minimumChannels": {
            "type": "array",
            "items": {
              "type": "string",
              "enum": [
                "Applications",
                "Websites"
              ]
            },
            "minItems": 0,
            "maxItems": 2,
            "uniqueItems": true
          },
          "recoveryReady": {
            "type": "boolean"
          },
          "startAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "endAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "expiresAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "OperationAuthorizationRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "resourceId",
          "intentDigest",
          "action"
        ],
        "properties": {
          "intentDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "action": {
            "type": "string",
            "enum": [
              "StrictCommit",
              "AccountDeletion",
              "MembershipRecovery",
              "GenerationReset",
              "DeviceRevocation"
            ]
          },
          "resourceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          }
        }
      },
      "OperationAuthorization": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "authorizationId",
          "action",
          "intentDigest",
          "deviceId",
          "expiresAtUtc",
          "serverSignature",
          "resourceId"
        ],
        "properties": {
          "authorizationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "action": {
            "type": "string",
            "enum": [
              "StrictCommit",
              "AccountDeletion",
              "MembershipRecovery",
              "GenerationReset",
              "DeviceRevocation"
            ]
          },
          "intentDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "expiresAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "serverSignature": {
            "$ref": "#/components/schemas/Signature"
          },
          "resourceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          }
        }
      },
      "StrictCommit": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "preparationId",
          "sessionId",
          "intentDigest",
          "ticketDigests",
          "operationAuthorization",
          "originDeviceId",
          "signature"
        ],
        "properties": {
          "preparationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "intentDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "ticketDigests": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/Digest"
            },
            "minItems": 1,
            "maxItems": 10,
            "uniqueItems": true
          },
          "operationAuthorization": {
            "$ref": "#/components/schemas/OperationAuthorization"
          },
          "originDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "CancelPreparation": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "preparationId",
          "sessionId",
          "intentDigest",
          "originDeviceId",
          "signature"
        ],
        "properties": {
          "preparationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "intentDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "originDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "StopNormal": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "accountId",
          "generationId",
          "commandId",
          "sessionId",
          "expectedSequence",
          "originDeviceId",
          "issuedAtUtc",
          "signature"
        ],
        "properties": {
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "commandId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "expectedSequence": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "originDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "issuedAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "ChannelHealth": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "channel",
          "state"
        ],
        "properties": {
          "channel": {
            "type": "string",
            "enum": [
              "Applications",
              "Websites"
            ]
          },
          "state": {
            "type": "string",
            "enum": [
              "Applied",
              "Missing",
              "Lost",
              "Unsupported"
            ]
          },
          "policyDigest": {
            "$ref": "#/components/schemas/Digest"
          }
        }
      },
      "ExecutionReceipt": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "accountId",
          "generationId",
          "sessionId",
          "commandId",
          "deviceId",
          "sequence",
          "requestedMode",
          "executionKind",
          "state",
          "observedAtUtc",
          "endAtUtc",
          "channels",
          "signature"
        ],
        "properties": {
          "protocol": {
            "const": "FG-ACK-1"
          },
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "commandId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sequence": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "requestedMode": {
            "$ref": "#/components/schemas/Mode"
          },
          "executionKind": {
            "type": "string",
            "enum": [
              "Real",
              "Limited",
              "Simulated",
              "Unsupported"
            ]
          },
          "state": {
            "type": "string",
            "enum": [
              "Received",
              "Prepared",
              "Applied",
              "AppliedDegraded",
              "Simulated",
              "Rejected",
              "Expired",
              "Stopped",
              "Interrupted",
              "RecoveryPending",
              "Recovered"
            ]
          },
          "policyDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "observedAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "endAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "channels": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/ChannelHealth"
            },
            "minItems": 0,
            "maxItems": 2,
            "uniqueItems": true
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "SyncSnapshot": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "accountId",
          "generationId",
          "membership",
          "keyEnvelopes",
          "documents",
          "tombstones",
          "sessions",
          "cursor",
          "serverTimeUtc",
          "resyncRequired"
        ],
        "properties": {
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "membership": {
            "$ref": "#/components/schemas/Membership"
          },
          "keyEnvelopes": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/KeyEnvelope"
            },
            "minItems": 0,
            "maxItems": 11,
            "uniqueItems": true
          },
          "documents": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/DocumentReceipt"
            },
            "minItems": 0,
            "maxItems": 50,
            "uniqueItems": true
          },
          "tombstones": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/Tombstone"
            },
            "minItems": 0,
            "maxItems": 50,
            "uniqueItems": true
          },
          "sessions": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/SessionResult"
            },
            "minItems": 0,
            "maxItems": 50,
            "uniqueItems": true
          },
          "cursor": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "nextCursor": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "serverTimeUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "resyncRequired": {
            "type": "boolean"
          }
        }
      },
      "DevicePreferenceRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "deviceId",
          "includedByDefault",
          "paused",
          "expectedRevision",
          "signature"
        ],
        "properties": {
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "includedByDefault": {
            "type": "boolean"
          },
          "paused": {
            "type": "boolean"
          },
          "expectedRevision": {
            "type": "integer",
            "minimum": 0,
            "maximum": 9007199254740991
          },
          "signature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "RevocationRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "deviceId",
          "membership",
          "keyEnvelopes",
          "rotationPending",
          "operationAuthorization"
        ],
        "properties": {
          "deviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "membership": {
            "$ref": "#/components/schemas/Membership"
          },
          "keyEnvelopes": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/KeyEnvelope"
            },
            "minItems": 0,
            "maxItems": 11,
            "uniqueItems": true
          },
          "rotationPending": {
            "type": "boolean"
          },
          "operationAuthorization": {
            "$ref": "#/components/schemas/OperationAuthorization"
          }
        }
      },
      "RecoveryRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "challengeId",
          "membership",
          "keyEnvelopes",
          "recoveryProof"
        ],
        "properties": {
          "challengeId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "membership": {
            "$ref": "#/components/schemas/Membership"
          },
          "keyEnvelopes": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/KeyEnvelope"
            },
            "minItems": 1,
            "maxItems": 11,
            "uniqueItems": true
          },
          "recoveryProof": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "ResetRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "challengeId",
          "newGenerationId",
          "membership",
          "keyEnvelopes",
          "lostKeysAcknowledged",
          "proof"
        ],
        "properties": {
          "challengeId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "newGenerationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "membership": {
            "$ref": "#/components/schemas/Membership"
          },
          "keyEnvelopes": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/KeyEnvelope"
            },
            "minItems": 1,
            "maxItems": 11,
            "uniqueItems": true
          },
          "lostKeysAcknowledged": {
            "const": true
          },
          "proof": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "DeletionRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "operationAuthorization",
          "activeStrictConsequencesAcknowledged"
        ],
        "properties": {
          "operationAuthorization": {
            "$ref": "#/components/schemas/OperationAuthorization"
          },
          "activeStrictConsequencesAcknowledged": {
            "const": true
          }
        }
      },
      "DeletionResult": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "state",
          "activeDeletionDeadlineUtc",
          "backupExpirationDeadlineUtc"
        ],
        "properties": {
          "state": {
            "const": "DeletionPending"
          },
          "activeDeletionDeadlineUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "backupExpirationDeadlineUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          }
        }
      },
      "ExportRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "includeCiphertext"
        ],
        "properties": {
          "includeCiphertext": {
            "type": "boolean"
          }
        }
      },
      "Export": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "account",
          "devices",
          "documents",
          "cursor"
        ],
        "properties": {
          "account": {
            "$ref": "#/components/schemas/Account"
          },
          "devices": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/Device"
            },
            "minItems": 0,
            "maxItems": 10,
            "uniqueItems": true
          },
          "documents": {
            "type": "array",
            "items": {
              "$ref": "#/components/schemas/CipherDocument"
            },
            "minItems": 0,
            "maxItems": 100,
            "uniqueItems": true
          },
          "cursor": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "nextCursor": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          }
        }
      },
      "AccountPatch": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "displayNameDocument"
        ],
        "properties": {
          "displayNameDocument": {
            "$ref": "#/components/schemas/CipherDocument"
          }
        }
      },
      "TimeResponse": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "serverTimeUtc"
        ],
        "properties": {
          "serverTimeUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          }
        }
      },
      "EventHint": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "generationId",
          "cursor",
          "kind"
        ],
        "properties": {
          "protocol": {
            "const": "FG-EVENT-1"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "cursor": {
            "type": "string",
            "minLength": 1,
            "maxLength": 256
          },
          "kind": {
            "type": "string",
            "enum": [
              "StateChanged",
              "MembershipChanged",
              "Revoked"
            ]
          }
        }
      },
      "PairingConfirmation": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "pairingId",
          "membershipDigest",
          "deviceSession"
        ],
        "properties": {
          "pairingId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "membershipDigest": {
            "$ref": "#/components/schemas/Digest"
          },
          "deviceSession": {
            "$ref": "#/components/schemas/DeviceSessionSecret",
            "description": "Secreto aleatorio del cliente; solo viaja por HTTPS y el servidor conserva su hash"
          }
        }
      },
      "DeviceSessionSecret": {
        "type": "string",
        "pattern": "^[A-Za-z0-9_-]{43}$",
        "minLength": 43,
        "maxLength": 43,
        "description": "32 bytes aleatorios base64url; secreto efímero de transporte, no clave E2EE"
      },
      "PairingState": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "pairingId",
          "deviceId",
          "state",
          "expiresAtUtc",
          "fingerprint",
          "membershipDigest",
          "membership",
          "keyEnvelopes",
          "membershipChain",
          "currentMembership",
          "currentMembershipDigest",
          "currentKeyEnvelopes",
          "currentMembershipChain"
        ],
        "properties": {
          "pairingId": { "$ref": "#/components/schemas/Id" },
          "deviceId": { "$ref": "#/components/schemas/Id" },
          "state": { "type": "string", "enum": ["Pending", "Approved", "Consumed", "Expired"] },
          "expiresAtUtc": { "$ref": "#/components/schemas/Timestamp" },
          "fingerprint": { "$ref": "#/components/schemas/Digest" },
          "membershipDigest": {
            "anyOf": [
              { "$ref": "#/components/schemas/Digest" },
              { "type": "string", "const": "" }
            ]
          },
          "membership": {
            "anyOf": [
              { "$ref": "#/components/schemas/Membership" },
              { "type": "null" }
            ]
          },
          "keyEnvelopes": {
            "anyOf": [
              { "type": "array", "items": { "$ref": "#/components/schemas/KeyEnvelope" }, "maxItems": 11 },
              { "type": "null" }
            ]
          },
          "membershipChain": {
            "type": "array",
            "items": { "$ref": "#/components/schemas/Membership" },
            "maxItems": 256
          },
          "currentMembership": {
            "anyOf": [
              { "$ref": "#/components/schemas/Membership" },
              { "type": "null" }
            ]
          },
          "currentMembershipDigest": {
            "anyOf": [
              { "$ref": "#/components/schemas/Digest" },
              { "type": "string", "const": "" }
            ]
          },
          "currentKeyEnvelopes": {
            "type": "array",
            "items": { "$ref": "#/components/schemas/KeyEnvelope" },
            "maxItems": 11
          },
          "currentMembershipChain": {
            "type": "array",
            "items": { "$ref": "#/components/schemas/Membership" },
            "maxItems": 256
          }
        }
      },
      "MembershipSnapshot": {
        "type": "object",
        "additionalProperties": false,
        "required": ["membership", "membershipDigest", "keyEnvelopes", "membershipChain"],
        "properties": {
          "membership": { "$ref": "#/components/schemas/Membership" },
          "membershipDigest": { "$ref": "#/components/schemas/Digest" },
          "keyEnvelopes": {
            "type": "array",
            "items": { "$ref": "#/components/schemas/KeyEnvelope" },
            "minItems": 1,
            "maxItems": 11
          },
          "membershipChain": {
            "type": "array",
            "items": { "$ref": "#/components/schemas/Membership" },
            "minItems": 1,
            "maxItems": 256
          }
        }
      },
      "PairingConfirmResult": {
        "type": "object",
        "additionalProperties": false,
        "required": ["pairingId", "state", "expiresAtUtc"],
        "properties": {
          "pairingId": { "$ref": "#/components/schemas/Id" },
          "state": { "type": "string", "const": "Consumed" },
          "expiresAtUtc": { "$ref": "#/components/schemas/Timestamp" }
        }
      },
      "RenewDeviceSessionRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": ["accountId", "generationId", "deviceId", "membershipDigest", "deviceSession"],
        "properties": {
          "accountId": { "$ref": "#/components/schemas/Id" },
          "generationId": { "$ref": "#/components/schemas/Id" },
          "deviceId": { "$ref": "#/components/schemas/Id" },
          "membershipDigest": { "$ref": "#/components/schemas/Digest" },
          "deviceSession": { "$ref": "#/components/schemas/DeviceSessionSecret" }
        }
      },
      "RenewDeviceSessionResult": {
        "type": "object",
        "additionalProperties": false,
        "required": ["expiresAtUtc", "snapshot"],
        "properties": {
          "expiresAtUtc": { "$ref": "#/components/schemas/Timestamp" },
          "snapshot": { "$ref": "#/components/schemas/MembershipSnapshot" }
        }
      },
      "NormalIntent": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "accountId",
          "generationId",
          "commandId",
          "sessionId",
          "originDeviceId",
          "targetDeviceIds",
          "membershipRevision",
          "keyEpoch",
          "issuedAtUtc",
          "startAtUtc",
          "endAtUtc",
          "mode",
          "normalLateJoinSeconds",
          "snapshot",
          "originSignature"
        ],
        "properties": {
          "protocol": {
            "const": "FG-INTENT-1"
          },
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "commandId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "originDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "targetDeviceIds": {
            "type": "array",
            "items": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            },
            "minItems": 1,
            "maxItems": 10,
            "uniqueItems": true
          },
          "membershipRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "issuedAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "startAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "endAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "mode": {
            "const": "Normal"
          },
          "normalLateJoinSeconds": {
            "type": "integer",
            "minimum": 0,
            "maximum": 86400
          },
          "snapshot": {
            "$ref": "#/components/schemas/SnapshotReference"
          },
          "originSignature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "StrictIntent": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "protocol",
          "accountId",
          "generationId",
          "commandId",
          "sessionId",
          "originDeviceId",
          "targetDeviceIds",
          "membershipRevision",
          "keyEpoch",
          "issuedAtUtc",
          "startAtUtc",
          "endAtUtc",
          "mode",
          "normalLateJoinSeconds",
          "snapshot",
          "originSignature"
        ],
        "properties": {
          "protocol": {
            "const": "FG-INTENT-1"
          },
          "accountId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "generationId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "commandId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "sessionId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "originDeviceId": {
            "type": "string",
            "format": "uuid",
            "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
          },
          "targetDeviceIds": {
            "type": "array",
            "items": {
              "type": "string",
              "format": "uuid",
              "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
            },
            "minItems": 1,
            "maxItems": 10,
            "uniqueItems": true
          },
          "membershipRevision": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "keyEpoch": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9007199254740991
          },
          "issuedAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "startAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "endAtUtc": {
            "type": "string",
            "format": "date-time",
            "pattern": "Z$",
            "maxLength": 24
          },
          "mode": {
            "const": "Strict"
          },
          "normalLateJoinSeconds": {
            "const": 0
          },
          "snapshot": {
            "$ref": "#/components/schemas/SnapshotReference"
          },
          "originSignature": {
            "$ref": "#/components/schemas/Signature"
          }
        }
      },
      "PrepareStrictRequest": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "intent"
        ],
        "properties": {
          "intent": {
            "$ref": "#/components/schemas/StrictIntent"
          }
        }
      }
    }
  }
}
```

## 6. Pruebas de contrato futuras

- Parseo/ref interno/operationId únicos; schemas cerrados y límite UTF-8.
- Paridad JCS con unicode/números; firma/verificación por los tres lenguajes.
- Negative cases mode cruzado, tiempos/nonce/generation inválidos, ACK de otro device, expirado, recovery no listo.
- CAS sin modificar AAD; stop/commit duplicados y cancel concurrente; TTL y cursor retention.
- Primero/segundo dispositivo sin círculo de autorización; secreto kit no aparece en body/log.
- Integración SQL/IdP reales: atomicidad de outbox/membresía/idempotencia y filtros de objetos.
No ejecutar ahora tests de enforcement: falta backend/cliente y se mantiene restricción no código.

