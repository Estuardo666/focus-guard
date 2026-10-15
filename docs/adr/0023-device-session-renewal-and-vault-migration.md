# ADR 0023 — Preserve legacy device identity and renew transport sessions

Date: 2026-10-12. Scope: Phase 2, Windows vault compatibility and bounded device transport authentication.

## Problem

Early Phase 2 vaults contain the original twelve FG-DEVICE-VAULT-1 fields. Later readers added epoch/history and device-session/pending-pairing fields under the same protocol label and rejected those previously valid encrypted vaults. A verified personal installation has such a readable DPAPI vault. Re-pairing with a new identity would discard the useful original trust and could require another device that the owner does not yet have.

## Decision

Accept only the exact known legacy twelve-field and intermediate fourteen-field shapes, as well as the current complete shape. Legacy content is epoch 1 with an empty historical key ring; absent transport-session and pending-pairing fields are null. Unknown fields and partially missing field groups remain invalid. Reading preserves identity, content key, recovery kit and membership context; it does not overwrite the file.

Add POST `/v1/bootstrap/sessions/renew`. It requires verified OIDC identity plus a token-bound, timestamped, single-use request proof signed by the already active device key. The body binds account/generation/device, the exact current membership digest and a fresh random transport secret. The signature domain is `FG-DEVICE-SESSION-RENEW-1`. A password or bearer token alone cannot renew, add a member or change an epoch.

The API locks the account, validates its signed current membership and the active device's Read role, rejects stale digests, records replay protection, and stores only the SHA-256 session hash. Renewal replaces prior transport sessions for that same active device without changing membership, content keys or permissions. An already active identical session hash can be acknowledged after a response was lost; a revoked or expired identical hash is never reactivated.

The native client persists the new random secret through the existing atomic DPAPI vault writer before sending its signed renewal. It then verifies the returned membership and performs the ordinary authenticated membership read before reporting linked. Ambiguous network failure preserves keys and the new secret so a later ordinary read can discover a successful commit. Renew only for missing/invalid transport sessions, never as a response to unavailable cloud, stale membership, revoked device or context mismatch.

## Gates

Legacy fixtures retain the same signing/encryption public keys, content key and recovery anchor. API integration must reject wrong account/key/digest, altered body, replay and revoked membership; old sessions stop working and new sessions expose only hashes in storage. Tests use synthetic accounts. The user's encrypted vault is not rewritten by diagnostics or development tooling; installed application migration remains an acceptance gate.
