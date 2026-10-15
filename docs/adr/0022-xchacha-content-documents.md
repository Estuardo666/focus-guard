# ADR 0022 — Canonical XChaCha20 content documents

Date: 2026-10-12. State: codec and C#/Node vectors implemented; synthetic C#/Android document vectors pass on the Android 15 AVD. Cloud document storage and Windows pairing UI remain future work.

## Decision

Implement the content suite selected in [ADR 0018](0018-cloud-e2ee-and-scoped-retention.md) as `FG-E2EE-1` using libsodium's `crypto_aead_xchacha20poly1305_ietf_*` functions. Content keys are 32 random bytes; each document uses a fresh 24-byte nonce and the 16-byte authentication tag appended to ciphertext. Keys are never derived from the OIDC password.

Plaintext is a UTF-8 JCS-canonical JSON object, at most 256 KiB, with maximum nesting depth 32. The codec rejects malformed UTF-8, noncanonical bytes, non-object roots, invalid IDs, unknown cryptographic suite labels, truncation, wrong keys and authentication failures. JCS strings are compared exactly; the codec does not normalize Unicode.

The encryption context contains `accountId`, `generationId`, `entityId`, `kind`, `baseRevision`, `proposedRevision`, `keyEpoch` and `membershipRevision`. UUIDs are lowercase. `kind` uses lowercase ASCII letters/digits/dot/hyphen. `proposedRevision` must equal `baseRevision + 1`; revisions and epochs stay within the JCS safe-integer range. AAD is the JCS serialization of `protocol`, `accountId`, `generationId`, `entityId`, `kind`, `baseRevision`, `proposedRevision`, `keyEpoch` and `membershipRevision`. Membership revision is authenticated by both AEAD and the signed header, matching the existing C# codec.

The signed envelope contains `protocol`, `suite`, the full encryption context, `authorDeviceId`, `nonce`, `ciphertext` and `signature`. The Ed25519 signature covers the JCS header excluding only `signature`, with domain separation `FG-DOCUMENT-1` followed by NUL. The codec verifies that header signature before attempting AEAD decryption. The caller must independently verify the signing key's active membership at the document revision; a valid signature alone does not authorize an author.

## Implementation and interoperability

`cloud/devices/Core/ContentDocumentCodec.cs` owns JCS header/AAD, context validation, signing and verification. `SodiumAead.cs` is a thin ABI binding to the official libsodium XChaCha20-Poly1305-IETF API; it copies and zeroes key, nonce, AAD and plaintext buffers around native calls. The production Windows publish carries the pinned NSec dependency and libsodium runtime.

`prototypes/cloud-phase0/content-document.test.mjs` and `DeviceInterop` test both C#→Node and Node→C# envelope exchange using libsodium-wrappers. The Android AVD probe now verifies C#→Android and Android→C# using the same synthetic content key/context, including `membershipRevision` AAD and signature binding. Tests cover wrong account/revision/epoch/author/key, changed ciphertext and noncanonical plaintext. These are codec vectors, not evidence that installed clients have synchronized or decrypted a cloud document.

## Boundaries

This ADR does not define cloud document persistence, CAS/ETag routes, event cursors, account settings schema, consent UI, migration, retention jobs or a Windows second-device enrollment flow. Server storage must treat ciphertext as opaque and bind the mutable server revision to the signed `baseRevision`/`proposedRevision` without rewriting ciphertext or AAD. These remain Phase 2/3 gates in the plan.
