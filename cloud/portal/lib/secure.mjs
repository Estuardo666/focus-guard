import { createCipheriv, createDecipheriv, createHash, randomBytes, timingSafeEqual } from 'node:crypto';
export const randomId = () => randomBytes(32).toString('base64url');
export const digest = value => createHash('sha256').update(value).digest('hex');
export function secretEquals(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length > 256 || b.length > 256) return false;
  const left = Buffer.from(a), right = Buffer.from(b);
  return left.length === right.length && timingSafeEqual(left, right);
}
export function encryptionKey(encoded) {
  const key = Buffer.from(encoded || '', 'base64');
  if (key.length !== 32 || key.toString('base64') !== encoded) throw new Error('BFF encryption key unavailable');
  return key;
}
export function seal(value, key, domain) {
  const iv = randomBytes(12), cipher = createCipheriv('aes-256-gcm', key, iv);
  cipher.setAAD(Buffer.from('FocusGuard.BFF.v1:' + domain));
  const clear = Buffer.from(JSON.stringify(value));
  try {
    if (clear.length > 65536) throw new Error('Session payload too large');
    const encrypted = Buffer.concat([cipher.update(clear), cipher.final()]);
    return Buffer.concat([iv, cipher.getAuthTag(), encrypted]).toString('base64');
  } finally { clear.fill(0); }
}
export function unseal(payload, key, domain) {
  if (typeof payload !== 'string' || payload.length > 90000) throw new Error('Invalid session payload');
  const bytes = Buffer.from(payload, 'base64');
  if (bytes.length < 28) throw new Error('Invalid session payload');
  const decipher = createDecipheriv('aes-256-gcm', key, bytes.subarray(0, 12));
  decipher.setAAD(Buffer.from('FocusGuard.BFF.v1:' + domain));
  decipher.setAuthTag(bytes.subarray(12, 28));
  const clear = Buffer.concat([decipher.update(bytes.subarray(28)), decipher.final()]);
  try { return JSON.parse(clear.toString('utf8')); } finally { clear.fill(0); }
}
export function cookie(request, name) {
  const matches = (request.headers.get('cookie') || '').split(';').map(p => p.trim()).filter(p => p.startsWith(name + '='));
  if (matches.length !== 1) return null;
  const value = matches[0].slice(name.length + 1);
  return /^[A-Za-z0-9_-]{43}$/.test(value) ? value : null;
}
export function safeOrigin(value) {
  const parsed = new URL(value);
  if (parsed.protocol !== 'https:' || parsed.username || parsed.password || parsed.pathname !== '/' || parsed.search || parsed.hash)
    throw new Error('Exact HTTPS portal origin required');
  return parsed.origin;
}
