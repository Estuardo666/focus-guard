import pg from 'pg';
let input = ''; for await (const chunk of process.stdin) input += chunk;
const credentials = JSON.parse(input);
for (const purpose of ['keycloak', 'product', 'bff']) {
  const client = new pg.Client({ connectionString: credentials[purpose].url, connectionTimeoutMillis: 15000 });
  try {
    await client.connect();
    const result = await client.query('SELECT count(*)::int AS tables FROM information_schema.tables WHERE table_schema NOT IN ($1,$2)', ['pg_catalog','information_schema']);
    console.log(`NEON ${purpose}: TLS connection OK, ${result.rows[0].tables} tables`);
    if (purpose === 'keycloak' && result.rows[0].tables > 0) {
      const realms = await client.query('SELECT name FROM realm'); console.log('REALMS ' + realms.rows.map(r => r.name).join(','));
      const locks = await client.query('SELECT locked,lockgranted FROM databasechangeloglock'); console.log('MIGRATION_LOCK ' + JSON.stringify(locks.rows));
      const active = await client.query("SELECT state,wait_event_type,wait_event FROM pg_stat_activity WHERE datname=current_database()");
      console.log('DB_WAIT ' + JSON.stringify(active.rows));
    }
  } catch (error) { console.log(`NEON ${purpose}: error code ${error.code || 'unknown'}`); process.exitCode=1; }
  finally { await client.end(); }
}
