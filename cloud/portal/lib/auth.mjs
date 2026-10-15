import * as oidc from 'openid-client';
import pg from 'pg';
import { cookie, digest, encryptionKey, randomId, safeOrigin, seal, secretEquals, unseal } from './secure.mjs';

const SESSION = '__Host-FG-Session', FLOW = '__Host-FG-Login';
let configurationPromise, pool;
function settings() {
  const origin = safeOrigin(process.env.PORTAL_ORIGIN);
  const issuer = new URL(process.env.OIDC_ISSUER);
  const localIdentityTest = process.env.NODE_ENV === 'test' && issuer.protocol === 'http:' &&
    issuer.hostname === '127.0.0.1' && issuer.port === '18443';
  if ((issuer.protocol !== 'https:' && !localIdentityTest) || issuer.username || issuer.password || issuer.search || issuer.hash)
    throw new Error('HTTPS identity required');
  if (!process.env.OIDC_CLIENT_SECRET || !process.env.DATABASE_URL || !process.env.FOCUSGUARD_API_ORIGIN)
    throw new Error('Account connection unavailable');
  const apiOrigin = safeOrigin(process.env.FOCUSGUARD_API_ORIGIN);
  return { origin, issuer, apiOrigin, key: encryptionKey(process.env.BFF_ENCRYPTION_KEY), localIdentityTest };
}
async function provider(s) {
  const options = s.localIdentityTest ? { execute: [oidc.allowInsecureRequests] } : undefined;
  configurationPromise ??= oidc.discovery(s.issuer, 'focusguard-bff', process.env.OIDC_CLIENT_SECRET, undefined, options).then(config => {
    oidc.enableNonRepudiationChecks(config);
    return config;
  }).catch(error => { configurationPromise = null; throw error; });
  return configurationPromise;
}
function database() {
  pool ??= new pg.Pool({ connectionString: process.env.DATABASE_URL, max: 2, ssl: { rejectUnauthorized: true }, connectionTimeoutMillis: 10000, statement_timeout: 15000 });
  return pool;
}
const cookieHeader = (name, value, age) => `${name}=${value}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${age}`;
function response(value, status = 200, cookies = []) {
  const headers = new Headers({ 'content-type': 'application/json', 'cache-control': 'no-store' });
  for (const c of cookies) headers.append('set-cookie', c);
  return new Response(JSON.stringify(value), { status, headers });
}
function redirect(url, cookies = []) {
  const headers = new Headers({ location: url.toString(), 'cache-control': 'no-store' });
  for (const c of cookies) headers.append('set-cookie', c);
  return new Response(null, { status: 303, headers });
}
async function withSession(request, s, work) {
  const id = cookie(request, SESSION);
  if (!id) return work(null, null, null);
  const client = await database().connect();
  try {
    await client.query('BEGIN');
    await client.query("SET LOCAL lock_timeout = '5s'");
    const result = await client.query('SELECT payload FROM focusguard_bff.sessions WHERE id_hash=$1 AND expires_at>NOW() AND idle_until>NOW() FOR UPDATE', [digest(id)]);
    const session = result.rows[0] ? unseal(result.rows[0].payload, s.key, 'session') : null;
    const output = await work(session, client, digest(id));
    await client.query('COMMIT');
    return output;
  } catch (error) { await client.query('ROLLBACK'); throw error; }
  finally { client.release(); }
}
async function access(session, client, id, s) {
  if (Date.now() + 30000 >= session.accessExpiresAt) {
    const config = await provider(s);
    const tokens = await oidc.refreshTokenGrant(config, session.refreshToken);
    session.accessToken = tokens.access_token;
    session.refreshToken = tokens.refresh_token || session.refreshToken;
    session.idToken = tokens.id_token || session.idToken;
    session.accessExpiresAt = Date.now() + Math.min(tokens.expires_in || 300, 3600) * 1000;
  }
  await client.query("UPDATE focusguard_bff.sessions SET payload=$1, idle_until=LEAST(expires_at,NOW()+INTERVAL '30 minutes') WHERE id_hash=$2", [seal(session, s.key, 'session'), id]);
  return session.accessToken;
}
export async function handleAuth(request, action) {
  try {
    const s = settings();
    const requestUrl = new URL(request.url);
    // Canonical origin is configured, never trusted from Host/X-Forwarded-Host.
    if (requestUrl.origin !== s.origin) return response({ error: 'OriginRejected' }, 400);
    if (action === 'login' && request.method === 'GET') {
      const config = await provider(s), id = randomId();
      const flow = { state: oidc.randomState(), nonce: oidc.randomNonce(), verifier: oidc.randomPKCECodeVerifier() };
      const challenge = await oidc.calculatePKCECodeChallenge(flow.verifier);
      await database().query("DELETE FROM focusguard_bff.flows WHERE expires_at<NOW()");
      await database().query("INSERT INTO focusguard_bff.flows(id_hash,payload,expires_at) VALUES($1,$2,NOW()+INTERVAL '5 minutes')", [digest(id), seal(flow, s.key, 'flow')]);
      const authorization = oidc.buildAuthorizationUrl(config, { scope: 'openid profile email', redirect_uri: s.origin + '/api/auth/callback', state: flow.state, nonce: flow.nonce, code_challenge: challenge, code_challenge_method: 'S256' });
      return redirect(authorization, [cookieHeader(FLOW, id, 300)]);
    }
    if (action === 'callback' && request.method === 'GET') {
      const id = cookie(request, FLOW);
      if (!id) return response({ error: 'LoginExpired' }, 400);
      const consumed = await database().query('DELETE FROM focusguard_bff.flows WHERE id_hash=$1 AND expires_at>NOW() RETURNING payload', [digest(id)]);
      if (!consumed.rows[0]) return response({ error: 'LoginExpired' }, 400, [cookieHeader(FLOW, '', 0)]);
      const flow = unseal(consumed.rows[0].payload, s.key, 'flow'), config = await provider(s);
      const tokens = await oidc.authorizationCodeGrant(config, requestUrl, { pkceCodeVerifier: flow.verifier, expectedState: flow.state, expectedNonce: flow.nonce, idTokenExpected: true });
      const claims = tokens.claims();
      const profile = await oidc.fetchUserInfo(config, tokens.access_token, claims.sub);
      const sid = randomId(), session = { accessToken: tokens.access_token, refreshToken: tokens.refresh_token, idToken: tokens.id_token,
        accessExpiresAt: Date.now() + Math.min(tokens.expires_in || 300, 3600) * 1000, csrf: randomId(),
        profile: { displayName: profile.name || '', email: profile.email || '', emailVerified: profile.email_verified === true } };
      if (!session.refreshToken) throw new Error('Session renewal unavailable');
      await database().query('DELETE FROM focusguard_bff.sessions WHERE expires_at<NOW() OR idle_until<NOW()');
      await database().query("INSERT INTO focusguard_bff.sessions(id_hash,payload,expires_at,idle_until) VALUES($1,$2,NOW()+INTERVAL '8 hours',NOW()+INTERVAL '30 minutes')", [digest(sid), seal(session, s.key, 'session')]);
      return redirect(s.origin, [cookieHeader(FLOW, '', 0), cookieHeader(SESSION, sid, 28800)]);
    }
    if (action === 'me' && request.method === 'GET') return withSession(request, s, async (session, client, id) => {
      if (!session) return response({ signedIn: false });
      const accessToken = await access(session, client, id, s);
      if (session.profile.emailVerified) {
        const api = await fetch(s.apiOrigin + '/v1/account', { headers: { authorization: 'Bearer ' + accessToken }, signal: AbortSignal.timeout(15000) });
        if (!api.ok) return response({ error: 'AccountUnavailable' }, api.status === 401 || api.status === 403 ? 401 : 503);
      }
      return response({ signedIn: true, ...session.profile, deviceLinked: false, csrf: session.csrf });
    });
    if (action === 'security' && request.method === 'GET') return withSession(request, s, async session => {
      if (!session) return redirect(s.origin);
      return redirect(s.issuer.toString().replace(/\/$/, '') + '/account/');
    });
    if (action === 'logout' && request.method === 'POST') {
      if (request.headers.get('origin') !== s.origin) return response({ error: 'CsrfRejected' }, 403);
      return withSession(request, s, async (session, client, id) => {
        if (!session) return response({ remoteRevoked: false }, 200, [cookieHeader(SESSION, '', 0)]);
        if (!secretEquals(request.headers.get('x-fg-csrf'), session.csrf)) return response({ error: 'CsrfRejected' }, 403);
        await client.query('DELETE FROM focusguard_bff.sessions WHERE id_hash=$1', [id]);
        let remoteRevoked = false;
        try {
          const closed = await fetch(s.issuer.toString().replace(/\/$/, '') + '/protocol/openid-connect/logout', {
            method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' },
            body: new URLSearchParams({ client_id: 'focusguard-bff', client_secret: process.env.OIDC_CLIENT_SECRET, refresh_token: session.refreshToken }), signal: AbortSignal.timeout(15000) });
          remoteRevoked = closed.ok;
        } catch { /* Local logout still commits; report remote status explicitly. */ }
        return response({ remoteRevoked }, 200, [cookieHeader(SESSION, '', 0)]);
      });
    }
    return response({ error: 'NotFound' }, 404);
  } catch { return response({ error: 'AccountConnectionUnavailable' }, 503); }
}
