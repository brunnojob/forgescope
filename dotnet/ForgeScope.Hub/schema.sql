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
CREATE TABLE IF NOT EXISTS forgescope.data_protection_keys (
  id uuid PRIMARY KEY, created_at timestamptz NOT NULL DEFAULT now(), xml text NOT NULL
);

CREATE TABLE IF NOT EXISTS forgescope.repo_watches (
  owner_id text NOT NULL, repository text NOT NULL, checked_at timestamptz,
  next_poll timestamptz NOT NULL DEFAULT now(), error text NOT NULL DEFAULT '',
  PRIMARY KEY(owner_id,repository)
);
CREATE TABLE IF NOT EXISTS forgescope.notifications (
  owner_id text NOT NULL, key text NOT NULL, repository text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(), document jsonb NOT NULL,
  is_read boolean NOT NULL DEFAULT false, saved boolean NOT NULL DEFAULT false,
  done boolean NOT NULL DEFAULT false, PRIMARY KEY(owner_id,key)
);
CREATE INDEX IF NOT EXISTS notifications_owner ON forgescope.notifications(owner_id,created_at DESC);
CREATE TABLE IF NOT EXISTS forgescope.monitored_runs (
  owner_id text NOT NULL, repository text NOT NULL, run_id bigint NOT NULL, attempt integer NOT NULL,
  PRIMARY KEY(owner_id,repository,run_id,attempt)
);
