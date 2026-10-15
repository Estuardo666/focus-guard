import canonicalize from 'canonicalize';
import sodium from 'libsodium-wrappers';

export const DOCUMENT_PROTOCOL = 'FG-E2EE-1';
export const DOCUMENT_SUITE = 'XCHACHA20-POLY1305-IETF';
const SIGNATURE_DOMAIN = 'FG-DOCUMENT-1';
await sodium.ready;

const encode = bytes => Buffer.from(bytes).toString('base64url');
const decode = value => {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_-]+$/.test(value)) throw new Error('Invalid base64url');
  const bytes = Buffer.from(value, 'base64url');
  if (encode(bytes) !== value) throw new Error('Noncanonical base64url');
  return bytes;
};
const canonical = value => {
  const text = canonicalize(value);
  if (typeof text !== 'string') throw new Error('JCS canonicalization failed');
  return text;
};
const requireContext = context => {
  const id = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
  for (const name of ['accountId', 'generationId', 'entityId']) if (!id.test(context[name])) throw new Error(`Invalid ${name}`);
  if (!/^[a-z][a-z0-9.-]{0,63}$/.test(context.kind)) throw new Error('Invalid kind');
  if (!Number.isSafeInteger(context.baseRevision) || context.baseRevision < 0 ||
      context.proposedRevision !== context.baseRevision + 1 || !Number.isSafeInteger(context.proposedRevision) ||
      !Number.isSafeInteger(context.keyEpoch) || context.keyEpoch < 1 ||
      !Number.isSafeInteger(context.membershipRevision) || context.membershipRevision < 1) throw new Error('Invalid revision or epoch');
};
const aad = context => canonical({
  protocol: DOCUMENT_PROTOCOL, accountId: context.accountId,
  generationId: context.generationId, entityId: context.entityId, kind: context.kind,
  baseRevision: context.baseRevision, proposedRevision: context.proposedRevision,
  keyEpoch: context.keyEpoch, membershipRevision: context.membershipRevision,
});
const header = document => canonical({
  protocol: document.protocol, suite: document.suite, accountId: document.accountId,
  generationId: document.generationId, entityId: document.entityId, kind: document.kind,
  baseRevision: document.baseRevision, proposedRevision: document.proposedRevision,
  keyEpoch: document.keyEpoch, membershipRevision: document.membershipRevision,
  authorDeviceId: document.authorDeviceId,
  nonce: document.nonce, ciphertext: document.ciphertext,
});
const signedBytes = bytes => Buffer.concat([Buffer.from(`${SIGNATURE_DOMAIN}\0`, 'ascii'), Buffer.from(bytes)]);

export function encryptContentDocument(document, contentKey, context, authorDeviceId, signingSecretKey) {
  requireContext(context);
  const clear = Buffer.from(canonical(document), 'utf8');
  if (clear.length === 0 || clear.length > 262144 || contentKey.length !== 32 || signingSecretKey.length !== 64)
    throw new Error('Content document size or key rejected');
  const nonce = sodium.randombytes_buf(24);
  const ciphertext = sodium.crypto_aead_xchacha20poly1305_ietf_encrypt(
    clear, Buffer.from(aad(context)), null, nonce, contentKey);
  const unsigned = {
    protocol: DOCUMENT_PROTOCOL, suite: DOCUMENT_SUITE, ...context, authorDeviceId,
    nonce: encode(nonce), ciphertext: encode(ciphertext), signature: encode(new Uint8Array(64)),
  };
  const signature = sodium.crypto_sign_detached(signedBytes(header(unsigned)), signingSecretKey);
  return { ...unsigned, signature: encode(signature) };
}

export function decryptContentDocument(envelope, contentKey, context, authorPublicKey) {
  requireContext(context);
  if (envelope.protocol !== DOCUMENT_PROTOCOL || envelope.suite !== DOCUMENT_SUITE ||
      envelope.authorDeviceId === undefined || authorPublicKey.length !== 32 || contentKey.length !== 32)
    throw new Error('Content document header rejected');
  for (const key of ['accountId', 'generationId', 'entityId', 'kind', 'baseRevision', 'proposedRevision', 'keyEpoch', 'membershipRevision'])
    if (envelope[key] !== context[key]) throw new Error(`Content document ${key} mismatch`);
  const bytes = Buffer.from(header({ ...envelope, signature: encode(new Uint8Array(64)) }));
  const signature = decode(envelope.signature);
  if (signature.length !== 64 || !sodium.crypto_sign_verify_detached(signature, signedBytes(bytes), authorPublicKey))
    throw new Error('Content document signature rejected');
  const plaintext = sodium.crypto_aead_xchacha20poly1305_ietf_decrypt(
    null, decode(envelope.ciphertext), Buffer.from(aad(context)), decode(envelope.nonce), contentKey);
  const text = Buffer.from(plaintext).toString('utf8');
  if (Buffer.from(text, 'utf8').compare(Buffer.from(canonical(JSON.parse(text)), 'utf8')) !== 0)
    throw new Error('Content document plaintext is not canonical JCS');
  return JSON.parse(text);
}
