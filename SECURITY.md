# Security

Normal scans parse/analyze source but do not execute it. Java uses an isolated compiler classpath with annotation processors disabled. Roslyn runs against framework metadata without project MSBuild, analyzers or source generators. Only an explicitly supplied worker DLL is launched. Use the trusted worker from this repository.

The ASP.NET Core workbench binds only to 127.0.0.1. It checks Host, validates Origin and a random nonce for scan POSTs, fixes the scan repository at startup, and passes arguments without a shell. It is not intended to be exposed on a network or placed behind a public proxy.

Patch validation intentionally executes explicit user-supplied commands on trusted projects. Temporary worktrees isolate edits, not machine/network privileges. Process-tree termination is best effort. No log-derived commands are run.

Inventory skips symlinks, common generated directories and `.env` names. This is not a complete secrets policy. Reports contain filenames, compiler diagnostics and evidence. Pattern-based redaction is incomplete; review all exports before sharing. No automatic uploads or cloud account features are included.

Use GitHub private vulnerability reporting when available. Do not attach secrets to public issues.
