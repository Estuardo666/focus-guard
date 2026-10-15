import pg from 'pg';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';

// Input/output credentials are piped privately by the PowerShell orchestrator.
// Never invoke this with a URL on argv or redirect its output into Git/plaintext files.
let input = '';
for await (const chunk of process.stdin) input += chunk;
const ownerUrl = new URL(input.trim());
if (!['postgres:', 'postgresql:'].includes(ownerUrl.protocol) || !ownerUrl.hostname.endsWith('.neon.tech'))
  throw new Error('Expected Neon PostgreSQL endpoint');
ownerUrl.searchParams.set('sslmode', 'verify-full');
const owner = new pg.Client({ connectionString: ownerUrl.toString(), connectionTimeoutMillis: 15000 });
const created = {};
try {
  await owner.connect();
  for (const purpose of ['keycloak', 'product', 'bff']) {
    const role = `focusguard_${purpose}`, database = `focusguard_${purpose}`;
    const exists = await owner.query('SELECT 1 FROM pg_roles WHERE rolname=$1', [role]);
    if (exists.rowCount) throw new Error('Role already exists; refusing credential rotation');
    const password = randomBytes(32).toString('hex');
    await owner.query(`CREATE ROLE "${role}" LOGIN PASSWORD '${password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT`);
    await owner.query(`GRANT "${role}" TO "${decodeURIComponent(ownerUrl.username)}"`);
    await owner.query(`CREATE DATABASE "${database}" OWNER "${role}"`);
    await owner.query(`REVOKE CONNECT ON DATABASE "${database}" FROM PUBLIC`);
    await owner.query(`GRANT CONNECT ON DATABASE "${database}" TO "${role}"`);
    const connection = new URL(ownerUrl); connection.username = role; connection.password = password; connection.pathname = '/' + database;
    created[purpose] = { role, database, password, url: connection.toString(), host: connection.hostname };
  }
  const bff = new pg.Client({ connectionString: created.bff.url, connectionTimeoutMillis: 15000 });
  await bff.connect();
  try { await bff.query(await readFile(new URL('../lib/schema.sql', import.meta.url), 'utf8')); }
  finally { await bff.end(); }
  process.stdout.write(JSON.stringify(created));
} catch {
  // No query text/connection credentials in diagnostics. Partial provisioning is
  // returned privately for recovery, never discarded after a later failure.
  process.stdout.write(JSON.stringify({ partial: created, failed: true }));
  process.exitCode = 1;
} finally { await owner.end(); }
