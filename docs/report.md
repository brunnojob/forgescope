# ForgeScope investigation

Project: sample

6 Python modules · 6 affected files · 12 findings

## Import-reachable test candidates

- tests/test_orders.py

## Evidence and actions

### Review this workflow privilege boundary
patchscope · .github/workflows/ci.yml · high
An added line uses broad write permissions or pull_request_target.
Use the minimum token permissions and keep untrusted code away from privileged execution.

### Python module is unavailable
devrescue · src/shop/orders.py · medium
ModuleNotFoundError: No module named &#x27;shop&#x27;
python -m pip --version; Activate the project virtual environment, then install its declared requirements.

### Review an unresolved local import
impact · src/shop/legacy.py · medium
shop.pricing.total_v1 -&gt; src/shop/pricing.py: Imported name is absent from the local module binding inventory.
Check spelling, removed/renamed definitions, re-exports and runtime module initialization. This is a static review lead, not a proven ImportError.

### GitHub Actions behavior changed
patchscope · .github/workflows/ci.yml · medium
Workflow changes can alter what executes and which credentials it receives.
Review triggers, permissions, action pinning and handling of untrusted pull requests.

### Dynamic code execution was added
patchscope · src/shop/pricing.py · medium
The added line calls eval or exec; context determines whether it is unsafe.
Trace input provenance and prefer a parser or structured operation where possible.

### Confirm behavior is covered
patchscope · src/shop/pricing.py · info
Source code changed, but this patch contains no changed test files. Existing tests may already cover it.
Run the relevant suite and add a regression test when behavior changes.

### Make checks repeatable
reporelief · no verified source location · high
No GitHub Actions workflow found
Add CI that runs the actual project checks on pushes and pull requests.

### Repair a missing local Markdown target
reporelief · README.md · medium
README.md:5: docs/setup.md -&gt; docs/setup.md
Restore the target or update the link relative to the source file directory. Only local path existence is checked.

### Document usage
reporelief · README.md · medium
README.md
Add a usage section with commands verified by a maintainer.

### Clarify reuse permissions
reporelief · no verified source location · medium
No root license file found
Choose and add a license appropriate to the project.

### Module could not be resolved
cifuse · no verified source location · medium
ModuleNotFoundError: No module named &#x27;shop&#x27;
Check the module name, declared dependencies, workspace install step and case-sensitive paths. Confirm the import exists on a clean checkout.

### Review modules without matching test names
reporelief · src/shop/__init__.py · medium
src/shop/__init__.py; src/shop/api.py; src/shop/legacy.py; src/shop/orders.py; src/shop/pricing.py
Check existing integration coverage first; add behavior tests for uncovered boundaries and failure paths. Filename and directory matching is a heuristic, not coverage.

## Limits

- Correlations are shared file evidence, not proof of root cause.
- Test candidates are import-reachable, not measured coverage.
- Redaction is best effort; inspect reports before sharing.
- RevScope recommends tools only; no reverse-engineering engine is included.