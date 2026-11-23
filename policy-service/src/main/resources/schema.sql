CREATE TABLE IF NOT EXISTS policy_evaluation (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    repository text NOT NULL,
    pull_request integer NOT NULL,
    head_sha text NOT NULL,
    base_sha text NOT NULL,
    conclusion text NOT NULL,
    evidence text NOT NULL,
    evaluated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS policy_evaluation_pr_idx
    ON policy_evaluation(repository, pull_request, id DESC);
