import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import sodium from 'libsodium-wrappers';
import canonicalize from 'canonicalize';
import { encryptContentDocument, decryptContentDocument } from './content-document.mjs';

await sodium.ready;
const context = {
  accountId: '11111111-1111-4111-8111-111111111111',
  generationId: '22222222-2222-4222-8222-222222222222',
  entityId: '33333333-3333-4333-8333-333333333333',
  kind: 'profile.settings', baseRevision: 4, proposedRevision: 5, keyEpoch: 3, membershipRevision: 7,
};
const document = { enabled: true, durationMinutes: 50, theme: 'dark' };
const contentKey = new Uint8Array(32).fill(73);
const signingSeed = Uint8Array.from({ length: 32 }, (_, i) => i + 31);
const author = sodium.crypto_sign_seed_keypair(signingSeed);
const authorDeviceId = '44444444-4444-4444-8444-444444444444';
const project = fileURLToPath(new URL('./DeviceInterop/FocusGuard.Devices.InteropProbe.csproj', import.meta.url));
const common = {
  accountId: context.accountId, generationId: context.generationId, entityId: context.entityId,
  kind: context.kind, baseRevision: context.baseRevision, proposedRevision: context.proposedRevision,
  keyEpoch: context.keyEpoch, membershipRevision: context.membershipRevision,
  authorDeviceId, signerDeviceId: authorDeviceId,
  recipientDeviceId: '55555555-5555-4555-8555-555555555555',
  recipientId: '55555555-5555-4555-8555-555555555555',
  membershipDigest: Buffer.alloc(32, 9).toString('base64url'),
  signerSigningSeed: Buffer.from(signingSeed).toString('base64url'),
  signerBoxSeed: Buffer.alloc(32, 97).toString('base64url'),
  recipientSigningSeed: Buffer.alloc(32, 129).toString('base64url'),
  recipientBoxSeed: Buffer.alloc(32, 161).toString('base64url'),
  contentKey: Buffer.from(contentKey).toString('base64url'),
  documentContext: context, documentJson: canonicalize(document),
};

test('XChaCha20-Poly1305 document seals canonical JSON to account, entity, revision and epoch', () => {
  const envelope = encryptContentDocument(document, contentKey, context, authorDeviceId, author.privateKey);
  assert.deepEqual(decryptContentDocument(envelope, contentKey, context, author.publicKey), document);
  assert.throws(() => decryptContentDocument(envelope, contentKey, { ...context, keyEpoch: 4 }, author.publicKey));
  assert.throws(() => decryptContentDocument({ ...envelope, authorDeviceId: '55555555-5555-4555-8555-555555555555' }, contentKey, context, author.publicKey));
  const ciphertext = Buffer.from(envelope.ciphertext, 'base64url');
  ciphertext[0] ^= 1;
  assert.throws(() => decryptContentDocument({ ...envelope, ciphertext: ciphertext.toString('base64url') }, contentKey, context, author.publicKey));
});

test('C# and libsodium-wrappers exchange signed XChaCha20-Poly1305 documents in both directions', () => {
  const fromNode = encryptContentDocument(document, contentKey, context, authorDeviceId, author.privateKey);
  const openInCsharp = execFileSync('dotnet', ['run', '--project', project, '-c', 'Release', '--no-build'], {
    input: JSON.stringify({ ...common, mode: 'node-document-to-csharp', contentEnvelope: fromNode }),
    encoding: 'utf8', timeout: 60000,
  });
  assert.match(openInCsharp, /"status":"PASS"/);

  const fromCsharp = execFileSync('dotnet', ['run', '--project', project, '-c', 'Release', '--no-build'], {
    input: JSON.stringify({ ...common, mode: 'csharp-document-to-node' }),
    encoding: 'utf8', timeout: 60000,
  });
  const result = JSON.parse(fromCsharp);
  assert.equal(result.status, 'PASS');
  assert.deepEqual(decryptContentDocument(result.envelope, contentKey, context, author.publicKey), document);
});
