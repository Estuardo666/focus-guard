import test from 'node:test';
import assert from 'node:assert/strict';
import { seal, unseal, encryptionKey, randomId, cookie, safeOrigin, secretEquals } from '../lib/secure.mjs';
const key = encryptionKey(Buffer.alloc(32, 7).toString('base64'));
test('encrypted sessions roundtrip without plaintext credentials', () => {
  const value = { refreshToken: 'synthetic-private-token' };
  const encrypted = seal(value, key, 'session');
  assert.deepEqual(unseal(encrypted, key, 'session'), value);
  assert.ok(!Buffer.from(encrypted, 'base64').includes(Buffer.from(value.refreshToken)));
});
test('tampered ciphertext and flow/session substitution rejected', () => {
  const encrypted = seal({ token: 'public-test' }, key, 'session');
  assert.throws(() => unseal(encrypted, key, 'flow'));
  const bytes = Buffer.from(encrypted, 'base64'); bytes[bytes.length - 1] ^= 1;
  assert.throws(() => unseal(bytes.toString('base64'), key, 'session'));
});
test('unsafe origins, malformed keys and oversized payload rejected', () => {
  for (const bad of ['http://example.com', 'https://example.com/path', 'https://user@example.com', 'https://example.com?x=1']) assert.throws(() => safeOrigin(bad));
  assert.equal(safeOrigin('https://example.com/'), 'https://example.com');
  assert.throws(() => encryptionKey('small'));
  assert.throws(() => seal({ large: 'x'.repeat(65536) }, key, 'session'));
});
test('opaque cookies are exact and duplicates are rejected', () => {
  const id = randomId();
  assert.equal(cookie(new Request('https://example.com', { headers: { cookie: '__Host-FG-Session=' + id } }), '__Host-FG-Session'), id);
  assert.equal(cookie(new Request('https://example.com', { headers: { cookie: `__Host-FG-Session=${id}; __Host-FG-Session=${id}` } }), '__Host-FG-Session'), null);
  assert.equal(cookie(new Request('https://example.com', { headers: { cookie: '__Host-FG-Session=invalid' } }), '__Host-FG-Session'), null);
});
test('CSRF comparison rejects absent and different values', () => {
  const id = randomId(); assert.ok(secretEquals(id, id)); assert.ok(!secretEquals(id, randomId())); assert.ok(!secretEquals(id, null));
});
