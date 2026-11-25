# ForgeScope notifications

## Browser workflow

1. Sign in with GitHub, open **Repositories**, then select **Monitor CI failures**.
2. Open **Notifications** and select **Sync now**, or wait for the background poll.
3. Filter by repository, unread, saved or done; search by message, workflow or source path.
4. Expand an alert to inspect its evidence. Source links use the run's head SHA, not the moving default branch. Run links include the attempt number.
5. Mark read, save, complete or reopen an alert. Completing an alert is inbox triage, not proof that the failure was fixed.

The inbox is ForgeScope's own UI. It does not alter GitHub's native notification inbox, send emails, or request desktop notification permission. The header offers English and Portuguese (Brazil), with language selection applied to navigation, forms, panels, explanations, statuses and browser error pages. Existing Spanish URLs remain compatible. Provider diagnostics, source identifiers and user-authored content retain their original text, with localized guidance beside them.

## Language coverage and evidence

Any repository language can be monitored. GitHub annotations provide path, line and message without depending on ForgeScope having an AST parser for that language. When a failed job has no failure annotations, bounded job logs are parsed for common compiler/linter locations (including C/C++, C#, Java, TypeScript, Go, Ruby, Python tracebacks and Rust formats). Formats without recognized locations stay as failed-step alerts. There is no claim of a verified root cause, a safe patch, or comprehensive language-specific analysis.

The Java/C# analyzers are independent capabilities, not a restriction on which repositories can be monitored. Existing security, dependency and infrastructure cost checks can surface their failure annotations in this inbox. Dedicated scanning, IaC pricing, PR check publication and automatic remediation are not added by the notification collector. Reviewed plans and trusted-runner validation remain in Investigations.

## Runtime and bounded collection

- Up to 20 monitored repositories per account. Polling is every two minutes, up to 10 due repository/account pairs per cycle. Manual synchronization processes up to five selected watches.
- Only active, unexpired OAuth sessions are used. Logout/expiry stops collection when no other valid session remains. Monitoring resumes on sign-in. Tokens remain encrypted by the existing data-protection setup.
- A database lease prevents concurrent instances from polling the same watch within two minutes. An immediate manual sync may therefore wait for the previous lease to expire.
- Each poll lists the latest 20 completed runs and considers up to five failed/timed-out/action-required runs. High-volume repositories can have missed failures between polls; this is a bounded collector, not a complete event archive. A skipped recent success does not auto-resolve an older failure.
- Up to 100 jobs per run, 20 failed jobs, the first 100 annotations per check, 20 parsed diagnostics per job, and 500 KB per job log. Up to 500 latest stored alerts are displayed; older entries remain in storage. A run/attempt with unavailable logs records a fallback; automatic retries of its diagnostic download are not performed.
- Only failure annotations are ingested, not every warning or security advisory. Failed step without location is explicitly distinguished from a diagnostic.
- Duplicate run attempts do not duplicate alerts or reset read/saved/done flags. Reruns have separate attempt identities.
- Polling requires the application instance to be running. A scale-to-zero deployment cannot promise continuous collection; use an always-running replica or a separately designed event worker for that requirement.
- Refresh the inbox page to display newly collected alerts. This version does not use WebSockets or browser push.

## Access, privacy and deployment

Public Actions/check endpoints work with the existing GitHub login. Private repository access requires the existing opt-in repo scope; this release does not expand OAuth permissions. A repository's accessibility is checked before adding a watch. All inbox reads and updates are scoped to the signed-in GitHub user and mutations require CSRF validation.

Inputs are HTML-escaped, common token/key/password patterns are redacted before persistence, and signed log downloads never receive the GitHub bearer token. Redaction is best effort, not a guarantee against every secret format. Do not send credentials to job logs. Repository source code, scripts, build hooks and tests are never executed by this collector.

Apply the additive migration with the new Hub image before enabling the updated application:

```sh
dotnet ForgeScope.Hub.dll --migrate
```

Use the same configured database and encrypted key setup as the Hub. The migration only adds ForgeScope-owned tables and indexes. No existing application data is deleted. If GitHub access or rate limits fail, the watch exposes a retry message rather than reporting the repository as healthy. Stopping a watch pauses collection; existing alerts remain available.

## Error panels and appearance

The inbox opens as an error dashboard. Its metrics reflect the current search and filters: distinct failed run attempts, diagnostics with source locations, failures without detail, and repositories represented. They are counts of collected evidence, not quality scores or complete coverage. Each error has a panel containing the original diagnostic, file/line when supplied, and a localized suggestion for what to inspect. Investigation reports use the same panel design.

Choose **Deep black**, **Soft black**, or **Light** in the header. Language and theme are stored in browser cookies and apply across Hub pages. Switching either preserves the current repository filter, search, page and selected alert. Cookies are specific to the browser, not synchronized between devices. Original CI messages are not rewritten or automatically translated.

### Existing Azure installation with a DML-only application role

The deployed `forgescope_app` role can use existing tables but cannot create new ones. Keep that restriction. Run [NOTIFICATION-MIGRATION.sql](NOTIFICATION-MIGRATION.sql) once as the existing schema owner/administrator; it creates only the three notification tables and grants their DML permissions to the application role. Do not grant database-wide CREATE or make the web application an administrator.

The migration executable skips CREATE SCHEMA when the schema already exists, but still requires ownership/CREATE rights for the objects being migrated. It is not intended to bypass a DML-only role. Run the normal Hub entrypoint without a migration command override after applying the SQL.
