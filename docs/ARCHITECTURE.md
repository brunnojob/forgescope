# Architecture

1. A bounded Python inventory reads UTF-8 text without following symlinks or importing target code.
2. `ast.parse` collects module definitions, import edges and syntactic call candidates. Reverse BFS stores an evidence chain from every selected/deleted module to each transitive importer. A graph cycle terminates through a visited set.
3. One Node subprocess receives JSON through stdin and calls vendored, commit-pinned engines. There are no shell commands, remote model calls or target config imports. Engine errors are recorded separately.
4. Findings normalize into stable identifiers, source engine, evidence, suggested action and optional verified source location. A location matching an import-affected file gains a correlation badge. Different engines are retained independently to preserve their provenance; matching findings are not claimed to be independent confirmation.
5. The HTML renderer uses textContent for report strings and escapes script delimiters before embedding JSON. The report is self-contained and uses no remote scripts. Graph nodes are a bounded preview; full paths and chains remain available below the map.
6. JSON/Markdown/HTML outputs and optional local SQLite history retain derived evidence only. Redaction is best effort, not a secret scanner.

## Integration scope

CIFuse source inspection/repair modules and DevRescue cloud accounts are not vendored into the scan. ForgeScope provides its own Python AST context and a separate explicit before/after patch validator. RevScope is a catalog, not a reverse engineering execution engine.

## Reproducibility

`forgescope/vendor/provenance.json` records every upstream SHA and copied path. `LICENSE` files are retained per component. Updating a component requires reviewing its API and license, refreshing provenance, then running integration fixtures and package tests. Never download engines dynamically at scan time.


## Lexical references in 0.2.0

`references.py` inventories bindings per module, function, class, lambda and comprehension. Conflicting bindings stop attribution; function bodies skip class namespaces when resolving free names. Qualified definition and caller names accompany syntactic call candidates. This does not infer object types or runtime dispatch. Local from-import names absent from a parsed module's binding inventory produce review leads, except when dynamic module exports or wildcard imports prevent that check. Runtime monkey-patching, decorators and import side effects remain outside this model.

Git comparison reads a verified local commit, disables external diff/textconv and fsmonitor execution, and captures a bounded tracked working-tree diff plus a NUL-delimited path list. Those paths remain impact seeds even when Node is unavailable. SARIF normalizes derived findings, with no location when a source file is unavailable.
