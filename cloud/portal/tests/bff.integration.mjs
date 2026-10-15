import assert from 'node:assert/strict';
import pg from 'pg';
import * as oidc from 'openid-client';
import { handleAuth } from '../lib/auth.mjs';
import { digest } from '../lib/secure.mjs';

const origin = 'https://focusguard.totemmassmedia.com';
const loginUrl = origin + '/api/auth/login';
const password = process.env.FG_BFF_TEST_PASSWORD;
if (!password) throw new Error('Synthetic BFF test account required.');
let checks = 0;
let flowHash;
let sessionHash;
function check(condition, label) {
  assert.ok(condition, label);
  checks++;
  console.log('PASS ' + label);
}
function setCookie(response, name) {
  const header = response.headers.getSetCookie().find(value => value.startsWith(name + '='));
  assert.ok(header, `${name} cookie missing`);
  return header.split(';', 1)[0];
}
function readCookie(header, name) {
  const value = header.split(';', 1)[0];
  assert.ok(value.startsWith(name + '='), `${name} cookie malformed`);
  return value;
}
function htmlDecode(value) {
  return value.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#39;|&#x27;/g, "'")
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>');
}
async function authenticate(authorization) {
  const cookies = new Map();
  const requested = new URL(authorization);
  assert.equal(requested.searchParams.get('client_id'), 'focusguard-bff');
  assert.equal(requested.searchParams.get('redirect_uri'), origin + '/api/auth/callback');
  assert.equal(requested.searchParams.get('response_type'), 'code');
  assert.equal(requested.searchParams.get('code_challenge_method'), 'S256');
  async function request(url, options = {}) {
    const headers = new Headers(options.headers);
    if (cookies.size) headers.set('cookie', [...cookies].map(([key, value]) => `${key}=${value}`).join('; '));
    const response = await fetch(url, { ...options, headers, redirect: 'manual' });
    for (const value of response.headers.getSetCookie()) {
      const [pair, ...attributes] = value.split(';');
      const index = pair.indexOf('=');
      if (index < 0) continue;
      const key = pair.slice(0, index), cookieValue = pair.slice(index + 1);
      if (attributes.some(attribute => attribute.trim().toLowerCase() === 'max-age=0')) cookies.delete(key);
      else cookies.set(key, cookieValue);
    }
    return response;
  }
  let page, url = authorization;
  for (let redirects = 0; redirects < 6; redirects++) {
    page = await request(url);
    if (page.status === 200) break;
    if (page.status === 400) {
      const html = await page.text();
      const diagnosis = /invalid parameter:\s*(client_id|redirect_uri|response_type|scope|code_challenge_method)/i.exec(html)?.[1]
        || (/invalid client/i.test(html) ? 'client' : /invalid scope/i.test(html) ? 'scope' : 'unclassified');
      throw new Error(`Keycloak authorization rejected HTTP 400 (${diagnosis})`);
    }
    assert.ok(page.status >= 300 && page.status < 400, `Keycloak login page returned ${page.status}`);
    url = new URL(page.headers.get('location'), url).toString();
    assert.equal(new URL(url).host, '127.0.0.1:18443', 'OIDC browser flow stayed on local fixture');
  }
  assert.equal(page.status, 200, 'Keycloak login form rendered');
  const html = await page.text();
  const form = html.match(/<form[^>]+id="kc-form-login"[^>]+action="([^"]+)"/);
  assert.ok(form, 'Keycloak login form found');
  const fields = new URLSearchParams();
  for (const input of html.matchAll(/<input\b[^>]*>/g)) {
    const tag = input[0];
    const name = tag.match(/\bname="([^"]+)"/);
    const value = tag.match(/\bvalue="([^"]*)"/);
    if (name && value) fields.set(name[1], htmlDecode(value[1]));
  }
  fields.set('username', 'phase1-unverified@example.invalid');
  fields.set('password', password);
  const endpoint = new URL(htmlDecode(form[1]), url);
  assert.equal(endpoint.host, '127.0.0.1:18443', 'login submission stayed on local fixture');
  const result = await request(endpoint, { method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: fields });
  if (result.status !== 302) {
    const failureHtml = await result.text();
    const reason = /invalid.*(code|session)|expired/i.test(failureHtml) ? 'session-or-code-expired'
      : /invalid.*(username|password)|invalid.*credentials/i.test(failureHtml) ? 'credentials-rejected'
      : /required.action|update.password/i.test(failureHtml) ? 'required-action'
      : 'unclassified';
    assert.fail(`synthetic account authentication returned HTTP ${result.status} (${reason}); response body omitted`);
  }
  const callback = new URL(result.headers.get('location'), endpoint);
  assert.equal(callback.origin, origin, 'Keycloak returns only to the registered BFF origin');
  assert.equal(callback.pathname, '/api/auth/callback');
  return callback;
}

const pool = new pg.Pool({ connectionString: process.env.DATABASE_URL, max: 1, ssl: { rejectUnauthorized: true } });
let sessionCookie;
try {
  const database = await pool.query('SELECT 1 AS ready');
  check(database.rows[0].ready === 1, 'BFF database role connects to Neon over TLS');
  const metadataResponse = await fetch(process.env.OIDC_ISSUER + '/.well-known/openid-configuration');
  assert.equal(metadataResponse.status, 200, 'Keycloak discovery is available');
  const metadata = await metadataResponse.json();
  check(metadata.issuer === process.env.OIDC_ISSUER, 'Keycloak discovery issuer matches the configured authority');
  for (const name of ['authorization_endpoint', 'token_endpoint', 'userinfo_endpoint', 'jwks_uri', 'end_session_endpoint']) {
    if (metadata[name]) {
      const endpoint = new URL(metadata[name]);
      assert.equal(endpoint.protocol, 'http:', `${name} uses the local-only fixture transport`);
      assert.equal(endpoint.host, '127.0.0.1:18443', `${name} remains on the local-only fixture`);
    }
  }
  check(true, 'all HTTP OIDC endpoints are confined to the loopback fixture');
  let configuration;
  try { configuration = await oidc.discovery(new URL(process.env.OIDC_ISSUER), 'focusguard-bff', process.env.OIDC_CLIENT_SECRET, undefined, { execute: [oidc.allowInsecureRequests] }); }
  catch (error) { console.error(`BFF_OIDC_DISCOVERY_FAILED=${error?.constructor?.name}:${error?.code || ''}:${error?.status || ''}`); throw error; }
  oidc.enableNonRepudiationChecks(configuration);
  check(true, 'openid-client discovers the real local Keycloak provider');

  const login = await handleAuth(new Request(loginUrl), 'login');
  if (login.status !== 303) throw new Error(`BFF login failed with HTTP ${login.status}: ${await login.text()}`);
  check(true, 'BFF starts authorization-code login');
  const flowCookie = readCookie(setCookie(login, '__Host-FG-Login'), '__Host-FG-Login');
  flowHash = digest(flowCookie.slice(flowCookie.indexOf('=') + 1));
  const authorization = login.headers.get('location');
  check(authorization.includes('code_challenge_method=S256') && authorization.includes('state='), 'BFF uses PKCE S256 and one-time state');

  const callbackUrl = await authenticate(authorization);
  const callback = await handleAuth(new Request(callbackUrl, { headers: { cookie: flowCookie } }), 'callback');
  check(callback.status === 303, 'BFF validates callback and creates server-side session');
  sessionCookie = readCookie(setCookie(callback, '__Host-FG-Session'), '__Host-FG-Session');
  sessionHash = digest(sessionCookie.slice(sessionCookie.indexOf('=') + 1));
  const stored = await pool.query('SELECT payload FROM focusguard_bff.sessions WHERE id_hash=$1', [sessionHash]);
  check(stored.rowCount === 1 && /^[A-Za-z0-9+/]+={0,2}$/.test(stored.rows[0].payload), 'Neon stores an encrypted session payload');

  const me = await handleAuth(new Request(origin + '/api/auth/me', { headers: { cookie: sessionCookie } }), 'me');
  const account = await me.json();
  check(me.status === 200 && account.signedIn === true, 'BFF restores the signed-in account');
  check(account.email === 'phase1-unverified@example.invalid' && account.emailVerified === false, 'BFF reports unverified email without granting verified account access');
  check(!JSON.stringify(account).toLowerCase().includes('token'), 'browser account response contains no tokens');

  const denied = await handleAuth(new Request(origin + '/api/auth/logout', { method: 'POST', headers: { cookie: sessionCookie, origin, 'x-fg-csrf': 'wrong' } }), 'logout');
  check(denied.status === 403, 'BFF rejects logout with invalid CSRF token');
  check((await pool.query('SELECT count(*)::int AS count FROM focusguard_bff.sessions WHERE id_hash=$1', [sessionHash])).rows[0].count === 1, 'rejected logout keeps the active session');

  const logout = await handleAuth(new Request(origin + '/api/auth/logout', { method: 'POST', headers: { cookie: sessionCookie, origin, 'x-fg-csrf': account.csrf } }), 'logout');
  const result = await logout.json();
  check(logout.status === 200 && result.remoteRevoked === true, 'BFF logs out and revokes the real Keycloak session');
  check((await pool.query('SELECT count(*)::int AS count FROM focusguard_bff.sessions WHERE id_hash=$1', [sessionHash])).rows[0].count === 0, 'BFF removes the server-side session on logout');
  const afterLogout = await handleAuth(new Request(origin + '/api/auth/me', { headers: { cookie: sessionCookie } }), 'me');
  check((await afterLogout.json()).signedIn === false, 'BFF rejects the old browser cookie after logout');
  console.log(`RESULT ${checks} BFF integration checks passed against real Keycloak and Neon.`);
} finally {
  if (sessionHash) await pool.query('DELETE FROM focusguard_bff.sessions WHERE id_hash=$1', [sessionHash]);
  if (flowHash) await pool.query('DELETE FROM focusguard_bff.flows WHERE id_hash=$1', [flowHash]);
  await pool.end();
}
