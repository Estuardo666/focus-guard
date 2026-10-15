CREATE SCHEMA IF NOT EXISTS focusguard_bff;
CREATE TABLE IF NOT EXISTS focusguard_bff.flows (
  id_hash char(64) PRIMARY KEY,
  payload text NOT NULL CHECK (octet_length(payload) <= 90000),
  expires_at timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS focusguard_bff.sessions (
  id_hash char(64) PRIMARY KEY,
  payload text NOT NULL CHECK (octet_length(payload) <= 90000),
  expires_at timestamptz NOT NULL,
  idle_until timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS flows_expiry ON focusguard_bff.flows(expires_at);
CREATE INDEX IF NOT EXISTS sessions_expiry ON focusguard_bff.sessions(expires_at);
