# Web source import

The Overview page accepts a project ZIP or pasted code after GitHub sign-in. The guided demonstration is public and explicitly uses example data. English and Portuguese are available throughout the flow; theme and language preferences persist.

`POST /source` validates authentication and CSRF, shares the existing scan rate limit, invokes the static analyzer and stores an account-scoped investigation. Uploaded code is never built or executed. Temporary source files are deleted in a `finally` block. Derived findings may contain code excerpts.

ZIP limits: 3 MB compressed, 10 MB extracted, 250 eligible files, 500 archive entries, 512 KB per file. Traversal, absolute paths, symbolic links and duplicate paths are rejected. Existing source exclusions skip credential filenames and generated folders. Pasted code requires a safe filename with extension and is limited to 200 KB UTF-8. There is no GitHub API call or invented commit for local uploads: the stored identity is a SHA-256 content digest, `Origin=local`. Local investigations do not offer GitHub source links or runner validation.

Repository-level checks apply across file types. Java and C# currently have dedicated semantic engines. Accepting a file does not imply full semantic or vulnerability coverage for its language. Reports expose engine status and limitations. No dependency advisory database or runtime security guarantee is added by this feature.

The error inbox groups related diagnostics by repository, execution attempt, job and failed step, prioritizes specific diagnostics over generic exit codes, and preserves the related evidence and per-alert state actions. Synthetic `.github` locations and generic exit statuses are not counted as source locations. HTTP 429 and missing deployment artifacts have localized guidance. External evidence links open a new tab.
