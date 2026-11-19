"""Bounded, conservative Python import and direct-call analysis; never imports targets."""
import ast
import os
from collections import deque
from pathlib import Path, PurePosixPath
from .references import import_base, inspect_references

IGNORED = {'.git', '.venv', 'venv', '__pycache__', 'node_modules', 'vendor', 'build', 'dist', '.forgescope', '.tox'}
MAX_FILES, MAX_FILE_BYTES, MAX_TOTAL_BYTES = 10000, 512000, 20000000

def inventory(root):
    root = Path(root).resolve()
    if not root.is_dir():
        raise ValueError('Repository must be a directory')
    files, skipped, total = {}, [], 0
    for base, dirs, names in os.walk(root, followlinks=False):
        dirs[:] = sorted(d for d in dirs if d not in IGNORED and not Path(base, d).is_symlink())
        for name in sorted(names):
            path = Path(base, name)
            rel = path.relative_to(root).as_posix()
            if name == '.env' or name.startswith('.env.') or path.is_symlink():
                skipped.append({'path': rel, 'reason': 'secret-name or symlink'})
                continue
            try:
                if path.stat().st_size > MAX_FILE_BYTES:
                    skipped.append({'path': rel, 'reason': 'file size limit'})
                    continue
                raw = path.read_bytes()
                if b'\0' in raw:
                    skipped.append({'path': rel, 'reason': 'binary'})
                    continue
                value = raw.decode('utf-8')
            except (OSError, UnicodeError) as exc:
                skipped.append({'path': rel, 'reason': type(exc).__name__})
                continue
            total += len(raw)
            if total > MAX_TOTAL_BYTES or len(files) >= MAX_FILES:
                raise ValueError('Inventory limit exceeded: 20 MB / 10,000 text files')
            files[rel] = value
    return files, skipped

def module_name(path):
    parts = list(PurePosixPath(path).with_suffix('').parts)
    if parts and parts[0] == 'src':
        parts.pop(0)
    if parts and parts[-1] == '__init__':
        parts.pop()
    return '.'.join(parts)

def is_test(path):
    p = PurePosixPath(path)
    return 'tests' in p.parts or 'test' in p.parts or p.name.startswith('test_') or p.name.endswith('_test.py')

def analyze(files, changed=(), symbol=None):
    modules, trees, warnings = {}, {}, []
    for path, text in files.items():
        if not path.endswith('.py'):
            continue
        name = module_name(path)
        modules.setdefault(name, []).append(path)
        try:
            trees[path] = ast.parse(text, filename=path)
        except (SyntaxError, ValueError, RecursionError) as exc:
            warnings.append({'path': path, 'reason': 'unparsed Python', 'line': getattr(exc, 'lineno', None)})
    def resolve(name):
        paths = modules.get(name, [])
        return paths[0] if len(paths) == 1 else None
    for path in changed:
        if path.endswith('.py') and path not in files:
            modules.setdefault(module_name(path), []).append(path)
            warnings.append({'path':path,'reason':'changed file absent; imports inferred from its module path only'})
    for name, paths in modules.items():
        if len(paths) > 1:
            warnings.append({'path': ', '.join(paths), 'reason': 'ambiguous module: ' + name})
    edges, edge_keys = [], set()
    def edge(source,target,line,imported,kind):
        key=(source,target,line,imported,kind)
        if key not in edge_keys:
            edge_keys.add(key)
            edges.append({'source':source,'target':target,'line':line,'import':imported,'kind':kind})
    for path, tree in trees.items():
        current = module_name(path)
        package = current if path.endswith('/__init__.py') else current.rpartition('.')[0]
        # Importing a package submodule initializes each parent package first.
        for size in range(1,len(current.split('.'))):
            name='.'.join(current.split('.')[:size]);dest=resolve(name)
            if dest and dest!=path and dest.endswith('/__init__.py'):
                edge(path,dest,1,name,'package-initialization')
        for node in ast.walk(tree):
            targets = []
            if isinstance(node, ast.Import):
                targets = [(a.name, a.name) for a in node.names]
            elif isinstance(node, ast.ImportFrom):
                base = import_base(node, package)
                if base is None:
                    warnings.append({'path':path,'line':node.lineno,'reason':'relative import goes beyond the known package root'})
                    continue
                for a in node.names:
                    fullname = '.'.join(filter(None,[base,a.name]))
                    targets.append((fullname if resolve(fullname) else base,fullname))
            for target, imported in targets:
                dest = resolve(target)
                if dest and dest != path:
                    edge(path,dest,node.lineno,imported,'import')
    calls, symbols, dynamic, unresolved = inspect_references(trees,modules,module_name)
    seeds = sorted(set(changed))
    if symbol:
        # A matching symbol narrows the starting files, never claims exact runtime callers.
        seeds = [p for p in seeds if any(s['path'] == p and (s['name'] == symbol or s['qualified_name'] == symbol) for s in symbols)]
        if not seeds:
            warnings.append({'reason': 'requested symbol not found in changed files', 'path': symbol})
    reverse = {}
    for e in edges:
        reverse.setdefault(e['target'], []).append(e)
    chains = {p: [] for p in seeds}
    queue = deque(seeds)
    while queue:
        target = queue.popleft()
        for edge in reverse.get(target, []):
            source = edge['source']
            if source not in chains:
                chains[source] = chains[target] + [edge]
                queue.append(source)
    affected = [{'path': p, 'distance': len(chain), 'chain': chain, 'test': is_test(p)} for p, chain in sorted(chains.items(), key=lambda x:(len(x[1]), x[0]))]
    return {'modules': len(trees), 'edges': edges, 'calls': calls, 'symbols': symbols, 'changed': seeds,
            'affected': affected, 'test_candidates': [x['path'] for x in affected if x['test']],
            'dynamic_sites': dynamic, 'warnings': warnings, 'unresolved_references': unresolved,
            'limits': 'Import reachability and syntactic calls only. No runtime dispatch, coverage proof or guarantee of breakage. Lexical bindings are conservative; runtime rebinding, external imports and plugins are not resolved. Missing-name leads are not proof of ImportError.'}
