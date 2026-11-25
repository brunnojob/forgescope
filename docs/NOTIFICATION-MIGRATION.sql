-- Run as the owner/administrator of the existing forgescope schema.
-- Creates only the notification tables. No database-wide CREATE grant is needed.
BEGIN;
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

GRANT SELECT, INSERT, UPDATE, DELETE ON forgescope.repo_watches, forgescope.notifications, forgescope.monitored_runs TO forgescope_app;
COMMIT;
