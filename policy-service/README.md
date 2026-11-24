# ForgeScope merge policy

The Spring Boot service polls open GitHub pull requests, reads `forgescope.yml` from the exact base commit, and publishes `ForgeScope / merge policy` on the PR head. PostgreSQL stores decision changes with both commit SHAs and the evidence posted to GitHub.

## Configure the App

Create and install a GitHub App on a test repository. Grant repository permissions for Checks (read and write), Contents (read), Pull requests (read) and Actions (read). The service polls GitHub and requires no incoming webhooks. Use one worker for the configured repositories.

Set these environment variables before starting the service.

| Variable | Value |
| --- | --- |
| `FORGESCOPE_APP_ID` | Numeric app ID |
| `FORGESCOPE_INSTALLATION_ID` | Numeric installation ID |
| `FORGESCOPE_PRIVATE_KEY_FILE` | Path to a PEM-encoded private key |
| `FORGESCOPE_PRIVATE_KEY_BASE64` | Base64 of a GitHub-generated PEM; preferred for secret-backed containers |
| `FORGESCOPE_REPOSITORIES` | Comma-separated GitHub repository names in owner/repo format |
| `SPRING_DATASOURCE_URL` | PostgreSQL JDBC URL |
| `SPRING_DATASOURCE_USERNAME` | Database user |
| `SPRING_DATASOURCE_PASSWORD` | Database password |

GitHub-generated PKCS#1 RSA keys and PKCS#8 keys are accepted directly. For a
local process, keep the key in a permission-restricted file.

```sh
chmod 600 github-app.pem
mvn -f policy-service/pom.xml clean verify
java -jar policy-service/target/policy-service-0.1.0.jar
```

## Set the base policy

Commit this file to the target branch first. Job names must match the name shown in the Actions run. Quote globs and use paths relative to the repository root.

```yaml
version: 1
rules:
  - id: payments-ci
    paths: ['payments/**']
    ci:
      workflow: ci.yml
      job: integration
      artifact: integration-evidence
  - id: protected-files
    paths: ['.github/workflows/**', 'forgescope.yml']
    approvals: 1
```

The workflow must run on `pull_request`. CI evidence must match the repository, PR head and base revision using the GitHub workflow run's PR association. The job must succeed on the current run attempt. The matching artifact must be unexpired and created on or after that attempt started. Artifacts establish presence only. Keep workflows and any scripts that produce trusted evidence under independent review.

Review approval applies to the current head and counts eligible human reviewers once. Configure GitHub's native review requirements as well when review dismissal or immediate enforcement is required.

## Verify a blocked and passing PR

1. In the test repository, open a PR touching a matching path. Configure the matching workflow to run a job named `integration` and initially omit `integration-evidence`.
2. Once the App has published its check, select `ForgeScope / merge policy` from this App as a required status check in the GitHub ruleset for the target branch. Require branches to be up to date before merge.
3. Confirm GitHub blocks the PR. Its check should link the run and report the missing artifact.
4. Push the fix that makes the job upload `integration-evidence` (or change the fixture so an already trusted workflow uploads it). Confirm the fresh CI run and ForgeScope check both pass.
5. Compare the decisions in PostgreSQL with the check output on each commit.

```sql
SELECT head_sha, base_sha, conclusion, evaluated_at, evidence
FROM policy_evaluation
WHERE repository = 'owner/test-repo' AND pull_request = 1
ORDER BY id;
```

Reconciliation is periodic (60 seconds by default). GitHub checks are attached to commits, so use a distinct head commit for each PR and keep native required reviews enabled where applicable. Use a test repository without a merge queue for this flow. Workflow runs whose PR association is absent cannot provide CI evidence in this release.

## Event-triggered checks

Set `FORGESCOPE_WEBHOOK_SECRET` to a shared random secret of at least 32
characters. The listener defaults to `127.0.0.1:8080`, configurable with
`FORGESCOPE_WEBHOOK_BIND` and `FORGESCOPE_WEBHOOK_PORT`. Route an HTTPS
reverse proxy to `POST /github/webhook` and set request size and read timeouts
at the proxy. Configure the App to deliver pull request, pull request review,
workflow run and push events. Verified deliveries for the configured
installation and repositories queue a refresh. Multiple deliveries can share a
refresh. Periodic reconciliation still runs if an event is missed.

The check includes matched paths, the review or CI requirement, a link to the CI
attempt, and steps to resolve failures. PatchScope and CIFuse add advisory
findings when a patch or supported Actions job log is available. The check shows
finding titles and counts and keeps raw logs out of GitHub and PostgreSQL output.

## Preview a candidate policy

```sh
java -jar policy-service/target/policy-service-0.1.0.jar simulate owner/repo candidate.yml
```

The command uses the same GitHub App credentials to read open PRs. It prints
current and candidate decisions in JSON, with the observed head and base SHA.
It does not start the web server, connect to PostgreSQL, or publish checks.
Run it separately for each repository. CI results, reviews and artifacts can
change while any preview is being generated.

## Reproducible live demo

Use a disposable test repo included in the App installation. Put
[demo/forgescope.yml](demo/forgescope.yml) at the root of the base branch, and
[demo/demo-ci.yml](demo/demo-ci.yml) in `.github/workflows/`. Open a non-draft
PR adding a nonempty `demo/fixture.txt`. Wait for the successful integration job
and the failed ForgeScope check. Require that check from the App on the base
branch, then record the blocked merge state and the PR head SHA. Push a second
commit adding a nonempty `demo/upload-evidence` file. The same workflow will
upload the artifact and ForgeScope should pass.

Use the authenticated GitHub CLI to collect links to the check runs and verify
the corrected PR is mergeable.

```sh
# Before pushing the fix
python3 policy-service/demo/verify.py owner/demo-repo PR_NUMBER BLOCKED_HEAD_SHA APP_SLUG --capture-blocked
# After the new GitHub check passes
python3 policy-service/demo/verify.py owner/demo-repo PR_NUMBER BLOCKED_HEAD_SHA APP_SLUG
```

## Run the public App

Build from the repository root.

```sh
docker build -f policy-service/Dockerfile -t forgescope-policy .
```

Set `FORGESCOPE_DISCOVER_INSTALLATIONS=true` for the public App. The worker
discovers its active installations and issues a distinct token per installation.
Each token is used only with repositories the installation can access. Keep
`FORGESCOPE_INSTALLATION_ID` and `FORGESCOPE_REPOSITORIES` unset on this worker.
The worker reconciles by polling. Use a separate single-installation instance
if you need the webhook or simulation setup documented above.

On Azure Container Apps, run one replica with ingress disabled and inject the
base64 App private key from a secret reference. Set the JDBC connection
to an accessible PostgreSQL database using `sslmode=verify-full` and a trusted
CA. Store database credentials as secret references. Use a database dedicated
to the policy service. Copy the SHA and corresponding check URL from each
phase of the live demo before enabling enforcement on a production branch.

The production template and deployment notes are in
[`infra/azure`](../infra/azure/README.md). The policy image is published to
`ghcr.io/brunnojob/forgescope-policy` when relevant changes reach `main`.
