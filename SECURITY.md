# Security

Normal scans do not import or execute the target project. They run bundled Node analysis code and parse repository text. Symlinks and `.env` filenames are skipped; this is not a complete secrets exclusion policy. Reports contain filenames and evidence snippets. Redaction is incomplete: inspect every report before publishing it.

Patch validation is opt-in and executes user-selected commands in a temporary checkout. It is not a process, network or filesystem sandbox. Use only trusted projects and commands. Subprocess descendants may outlive a timeout. No cloud upload or account integration is implemented.

Do not publish exploitable vulnerability details or secrets in public issues. Use the repository's private vulnerability reporting facility when available; otherwise contact the maintainer through their public GitHub profile to arrange disclosure without attaching secrets.
