<div align="center">

# ForgeScope

### Evidence before edits.

**What could this change affect — and where should I investigate first?**

[Explore the example report](https://brunnojob.github.io/forgescope/) · [Português](README.pt-BR.md) · [Architecture](docs/ARCHITECTURE.md) · [Contribute](CONTRIBUTING.md)

[![CI](https://github.com/brunnojob/forgescope/actions/workflows/ci.yml/badge.svg)](https://github.com/brunnojob/forgescope/actions/workflows/ci.yml)
![Python](https://img.shields.io/badge/Python-3.11%2B-3776ab)
![Node](https://img.shields.io/badge/integrated_engines-Node_22%2B-7fa95b)
![License](https://img.shields.io/badge/license-MIT-a6efac)

</div>

Changing unfamiliar code means reconstructing dependencies, reviewing a patch, reading CI logs and finding missing maintenance work across separate tools. ForgeScope brings those evidence streams into one local investigation report, with a new Python import-impact analyzer at its center.

It integrates the actual analysis engines from **PatchScope, CIFuse, RepoRelief and DevRescue**, plus the **RevScope tool catalog**. The report relates findings to changed and import-affected files. A shared file is evidence of a connection, not proof of a root cause.

## See a complete example

The [interactive report](https://brunnojob.github.io/forgescope/) is generated from the [fictional shop fixture](examples/sample), [unified diff](examples/change.diff) and [CI log](examples/build.log).

- `pricing.py` is imported by `orders.py`.
- `api.py` and `tests/test_orders.py` import `orders.py`.
- The patch introduces dynamic evaluation and broad workflow permissions.
- The log contains a missing-module failure at `orders.py`.
- The report shows import chains, a test candidate, diff prompts, grouped log diagnostics and maintenance findings together.

These are reproducible fixture results, not production measurements or promises that every affected file will break.

## Quick start

Requires **Python 3.11+** and **Node.js 22+** for the bundled upstream engines. The Python core uses only the standard library. No npm install, model, account, token or cloud service is needed.

```sh
git clone https://github.com/brunnojob/forgescope.git
cd forgescope
python -m forgescope examples/sample \
  --changed src/shop/pricing.py \
  --diff examples/change.diff --log examples/build.log \
  --output .forgescope
```

Open `.forgescope/index.html` directly. It works offline and includes a dependency map, engine/search filters, evidence details, light/dark themes, English/Portuguese interface controls and JSON download. Engine evidence retains the original upstream wording and language.

```sh
# Inspect your own repository without executing its code
python -m forgescope /path/to/project --changed src/package/service.py

# A supplied diff also seeds impact analysis from its file paths
python -m forgescope /path/to/project --diff /path/to/change.diff --log /path/to/ci.log

# Narrow the starting file to a named definition; downstream impact remains file-level
python -m forgescope /path/to/project --changed app.py --symbol handle_request

# Compare stable finding identities; optionally save sanitized derived reports locally
python -m forgescope /path/to/project --baseline previous/report.json \
  --history /path/to/history.sqlite --output next-report
```

Each run writes `report.json`, `report.md` and a self-contained `index.html`. History is opt-in. No raw source inventory or full CI log is included in exports, but filenames and evidence snippets can still be sensitive. Inspect reports before sharing.

## One investigation, six capabilities

| Capability | Implementation | Boundary |
| --- | --- | --- |
| Python impact | New AST import graph, direct syntactic call candidates, reverse reachability and source lines | Not runtime dispatch, measured coverage or exact function-level impact |
| Diff review | Vendored PatchScope engine | Heuristic review prompts, not a security audit |
| CI grouping | Vendored CIFuse engine | Pattern matches and investigation hints, not automatic root cause proof |
| Troubleshooting | Vendored DevRescue root engine | Evidence, available source locations and suggested steps; no commands executed |
| Repository upkeep | Vendored RepoRelief engine | Documentation, links, filename-based test signals, CI/license checks |
| Reverse engineering guidance | Vendored RevScope catalog with artifact-based suggestions | No decompiler, debugger, binary execution or firmware extraction |

Vendored engines are pinned to the exact commits in [provenance.json](forgescope/vendor/provenance.json), with original MIT licenses preserved. Node is invoked through one JSON bridge. The normal scan does not run target code, repository configurations, suggested commands or tests. If Node is missing or an engine fails, its status is visible; the Python graph remains available. A missing diff is marked skipped.

## Validate a reviewed patch

Use a clean, committed **trusted** Git repository. This separate command intentionally executes the explicit test command before and after applying your supplied patch in a disposable detached worktree:

```sh
python -m forgescope.validation /path/to/trusted/project /path/to/reviewed.patch \
  --test-command '["python","-m","unittest","discover","-s","tests"]' \
  --output validation.json
```

Validation succeeds only when the before command fails and the after command passes. Build artifacts are removed from the temporary checkout between runs. The original checkout is preserved. This isolates edits, **not execution privileges**: trusted tests can access the machine and network, and subprocesses they launch may outlive a timeout. No arbitrary command from a log is run, no patch is automatically generated, and no PR is automatically opened. A passing selected test command does not establish full correctness.

## Limits you can inspect

- Python root and conventional `src/` layouts; absolute and relative imports, aliases and transitive file dependents. Multiple candidate module paths are marked ambiguous and not guessed.
- Function selection filters starting files; import chains remain conservative at file level. Calls may be shadowed. No polymorphic dispatch, dynamic import resolution, cross-language call graph or external package graph.
- Known deleted `.py` paths can seed existing importers; the deleted source body is unavailable.
- Parse errors, dynamic sites and skipped files are visible. Inventory excludes symlinks, `.env` names and common generated directories, and caps scans at 10,000 text files / 20 MB / 512 KB per file. Logs: 20 files / 2 MB total. Diff: 2 MB, with PatchScope's own file/line limits.
- Redaction is pattern based and incomplete. No security or quality percentage is invented. Finding comparison reports new/existing/absent evidence, not verified repair.
- No cloud accounts, Azure database migration or hosted report synchronization is transplanted from DevRescue. Existing projects remain independent.

## Development

```sh
python -m unittest discover -s tests -v
python -m forgescope examples/sample --diff examples/change.diff \
  --log examples/build.log --output docs --fail-on engine-error
```

CI tests Python 3.11/3.13 on Linux and Windows, builds an installable wheel, verifies packaged engine data, and regenerates the fixture report. Pages publishes only the fictional example. Do not substitute private source or logs into that public demo workflow.

## Credits

Built on [PatchScope](https://github.com/brunnojob/patchscope), [CIFuse](https://github.com/brunnojob/cifuse), [RepoRelief](https://github.com/brunnojob/reporelief), [DevRescue](https://github.com/brunnojob/devrescue) and [RevScope](https://github.com/brunnojob/revscope), maintained by Brunno. Original engine files and catalog are preserved with their own licenses. New orchestration, Python analysis and viewer: MIT.

God first. Build useful things, and make the evidence inspectable.
