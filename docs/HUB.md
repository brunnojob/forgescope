# ForgeScope Hub: GitHub identity, investigation, review and validation

The Hub adds a hosted ASP.NET Core application to the existing Java/.NET analyzers. It is a separate executable from the loopback-only Workbench. The Pages demonstration and local CLI continue to work without accounts.

## Working flow

1. Sign in with your own GitHub account using the OAuth authorization-code flow, S256 PKCE and a correlation cookie. No GitHub password is collected by ForgeScope.
2. Browse the repositories visible to that authorization. Start an investigation from a branch snapshot or PR, optionally with a GitHub Actions run ID and/or pasted CI log.
3. The server reads a bounded snapshot at an immutable commit and runs the existing Java rule ports and Roslyn worker. No target builds, generators, tests, binaries or repository scripts execute during import. Diff, CI, maintenance, impact and RevScope evidence appear together.
4. Write a repair plan and supply a unified patch. This is a human-authored/reviewed correction, not an automatic generated repair. Comments anchor to plan lines. Unresolved blockers prevent approval.
5. Approve the exact revision, then queue its patch. Plan, patch and source commit share a digest. Changing either plan or patch starts a pending revision; old feedback and decisions remain in history.
6. Create a one-hour runner token and run the explicit local command below in a clean, trusted checkout. The runner processes one queued job and exits. There is no background scheduling.
7. Java validates the reviewed patch in a disposable detached Git worktree with the test argv you supply. Only failing-before/passing-after results can become `validated`. The result binds to the job, reviewed digest, patch digest and base commit. Old results cannot validate new revisions.

The dashboard contains recent investigation cards, review state, recorded CI status, findings and validation results. CI evidence from another SHA is explicitly labeled as such. Download the complete investigation as JSON to retain its evidence, revisions, comments, events and validation results.

## Deploy using the existing Azure PostgreSQL

The repository does **not** create paid Azure resources, change an existing database, register an OAuth App or assume a live connection. Build and run the server in an existing authorized container host. Pages cannot host this executable.

Create a dedicated GitHub OAuth App under your account at <https://github.com/settings/developers>. Use your actual Hub origin as the homepage and **`https://YOUR-HUB/signin-github`** as the callback URL. In Azure, configure the following server settings through secret references, not Git or browser source:

| Setting | Value |
| --- | --- |
| `FORGESCOPE_PUBLIC_URL` | Canonical HTTPS Hub origin, with no subpath |
| `GITHUB_CLIENT_ID` | The OAuth App client ID |
| `GITHUB_CLIENT_SECRET` | The OAuth App client secret, stored as a server secret |
| `FORGESCOPE_DATABASE` | Npgsql connection string for the existing database, with `SSL Mode=VerifyFull`; install the required trusted CA on the host |
| `FORGESCOPE_KEYS` | Private, persistent writable directory for ASP.NET Data Protection keys, shared across replicas |
| `FORGESCOPE_PRIVATE_REPOS` | Optional `true` requests GitHub's broad OAuth `repo` scope; otherwise only `read:user` is requested |
| `FORGESCOPE_JAR` | Absolute path to built Java JAR (provided by Dockerfile) |
| `FORGESCOPE_WORKER` | Absolute path to Roslyn DLL (provided by Dockerfile) |

GitHub OAuth's `repo` scope is broad; the login screen discloses when it is enabled. The Hub makes read-only GitHub requests and does not publish PRs or merge changes. Fine-grained GitHub App installations are a future alternative, not implemented here. Expiring OAuth credentials end the session at their lifetime or eight hours, whichever is shorter; sign in again instead of retaining refresh tokens.

Build:

```sh
docker build -t forgescope-hub .
```

Before serving requests, apply the **additive** schema explicitly by launching the same image/configuration with `--migrate`. `dotnet/ForgeScope.Hub/schema.sql` creates only the `forgescope` namespace and its three tables/index. Review it and grant the deployment identity the necessary rights. Normal startup never migrates automatically. Existing DevRescue tables and data remain outside this schema. After migration, the runtime identity needs access only to these tables.

Use a private persistent volume for the key directory and protect it with host storage encryption, permissions and backups. The application encrypts OAuth tokens using those keys; they are not stored in browser storage or authentication cookies. Losing the keys invalidates sessions and makes stored tokens unreadable. The framework cookie contains identity/session references only. Logout removes the server session. Expired session rows can be removed by your existing maintenance policy; no maintenance schedule is installed.

Configure the reverse proxy to preserve the canonical Host, expose HTTPS, and allow analysis requests up to five minutes. The app uses the configured origin for OAuth callbacks and rejects a foreign Host. It does not trust arbitrary forwarded headers. `/health` reports process liveness; `/ready` checks the PostgreSQL schema. No CORS policy is needed because the Hub serves its own UI on the same origin.

For local development, use an HTTP loopback origin and callback, local PostgreSQL and a private key directory. Build the Hub and worker with .NET 10 and the Java JAR with `java tools/Build.java`, then run the DLL. Environment variables are read directly; `.env` files are not automatically sourced.

## Local validation runner

After reviewing and approving a patch, queue it and create its runner token in the investigation page. Set `FORGESCOPE_RUNNER_TOKEN` in your local process environment. The token is shown once, stored only as a SHA-256 digest by the Hub, scoped to that investigation and expires after one hour.

```sh
dotnet dotnet/ForgeScope.Hub/bin/Release/net10.0/ForgeScope.Hub.dll \
  --runner https://YOUR-HUB \
  --repository owner/repo \
  --checkout /absolute/trusted/checkout \
  --jar /absolute/path/build/forgescope.jar \
  --test-command '["dotnet","test","--no-restore"]'
```

The origin must identify that GitHub repository and HEAD must match the recorded source commit. For fork PRs, use a trusted checkout whose origin identifies the selected base repository and explicitly fetch/check out the reviewed head commit before running. This is your deliberate trust decision; the Hub never checks out a PR and runs its tests on the cloud host. The supplied command must work in a clean temporary checkout, including dependency preparation if needed. GitHub credentials are not required by the runner.

This is **edit isolation, not an execution sandbox**. Tests run with your local permissions and network access. The test command comes only from your local argv, never from logs, a plan or the server. Validation is an authenticated runner's report, not remote attestation or proof of correctness beyond those tests. Timeout/crash results do not validate a patch. Cancel a stuck job in the UI before retrying; cancellation invalidates its lease but cannot stop a process already running on another computer. Runner leases expire after 30 minutes and only accept one completion.

## Scope and limits

- The integrated analyzers retain the limitations in the root README. This does not claim full upstream parity, packet decoding, decompilation or automatic general-purpose repair.
- Hosted import supports at most 250 eligible files, 512 KB per file and 10 MB total. Truncated GitHub trees and oversized snapshots fail explicitly. Symlinks, `.env*` and common generated directories are excluded before download. A repository scan is a bounded snapshot, not the complete Git history.
- GitHub Actions logs use a signed HTTPS download with no forwarded GitHub token. ZIP downloads are capped at 10 MB, decoded `.txt` content at 2 MB / 100 entries. No archive entries are extracted to disk. Expired logs, rate limits and unsupported download hosts fail explicitly. Pasted logs are also capped at 2 MB combined. A selected run may describe a merge commit rather than the PR head; the UI shows the mismatch.
- Large projects can import a local `report.json` with a declared source commit. The Hub checks GitHub repository access but does not independently verify the report's content or declared commit. Imported reports are recorded as unverified local submissions.
- Source snapshots, raw logs and temporary reports are deleted after an import attempt. Stored derived evidence may still contain sensitive paths/snippets: redaction is incomplete. Plans and supplied patches are stored privately because review/validation depends on their exact content. Review before uploading and exporting.
- Authentication is per numeric GitHub identity. All investigation reads, writes and exports are owner scoped. This initial Hub has no shared team reviews, role administration, live webhook notifications, automatic repair PR publication or tamper-proof audit trail.
- All new UI screens support English, PT-BR and Spanish, with light/dark preferences. Source evidence, machine identifiers, CI states and audit event codes remain original. Browser pages use server-rendered forms and no custom JavaScript.

## Verification

```sh
dotnet build dotnet/ForgeScope.Hub.Tests -c Release -m:1
dotnet dotnet/ForgeScope.Hub/bin/Release/net10.0/ForgeScope.Hub.dll --self-test
# Set FORGESCOPE_TEST_DATABASE to a disposable local PostgreSQL database first.
dotnet dotnet/ForgeScope.Hub.Tests/bin/Release/net10.0/ForgeScope.Hub.Tests.dll \
  build/forgescope.jar dotnet/ForgeScope.Worker/bin/Release/net10.0/ForgeScope.Worker.dll
```

The integration suite uses a fake GitHub HTTP transport for OAuth/API protocol checks, a real PostgreSQL database, the actual Java/Roslyn analyzer and actual Git worktree test execution. No production credentials/database are used. CI also builds the container. A passing suite does not verify your registered OAuth App, Azure connection, ingress or deployed secrets; those require a real deployment smoke test.

GitHub OAuth reference: <https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps>.
