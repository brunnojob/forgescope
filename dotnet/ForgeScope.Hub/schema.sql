-- Apply explicitly with --migrate. Additive, isolated from existing application schemas.
CREATE SCHEMA IF NOT EXISTS forgescope;
CREATE TABLE IF NOT EXISTS forgescope.sessions (
  id text PRIMARY KEY, user_id text NOT NULL, login text NOT NULL,
  protected_token text NOT NULL, expires_at timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS forgescope.investigations (
  id uuid PRIMARY KEY, owner_id text NOT NULL, repository text NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now(), version integer NOT NULL DEFAULT 1,
  document jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS investigations_owner ON forgescope.investigations(owner_id, updated_at DESC);
CREATE TABLE IF NOT EXISTS forgescope.runner_tokens (
  digest text PRIMARY KEY, owner_id text NOT NULL, investigation_id uuid NOT NULL
    REFERENCES forgescope.investigations(id), expires_at timestamptz NOT NULL
);
