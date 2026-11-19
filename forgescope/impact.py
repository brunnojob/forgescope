"""Bounded, conservative Python import and direct-call analysis; never imports targets."""
import ast
import os
from collections import deque
from pathlib import Path, PurePosixPath

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
    edges, calls, symbols, dynamic = [], [], [], []
    for path, tree in trees.items():
        current = module_name(path)
        package = current if path.endswith('/__init__.py') else current.rpartition('.')[0]
        aliases = {}
        for node in ast.walk(tree):
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                symbols.append({'path': path, 'name': node.name, 'line': node.lineno, 'kind': type(node).__name__})
            targets = []
            if isinstance(node, ast.Import):
                for a in node.names:
                    targets.append((a.name, a.name))
                    aliases[a.asname or a.name.split('.')[0]] = a.name if a.asname else a.name.split('.')[0]
            elif isinstance(node, ast.ImportFrom):
                if node.level:
                    parts = package.split('.') if package else []
                    prefix = parts[:len(parts) - node.level + 1] if node.level <= len(parts) else []
                    base = '.'.join(prefix + ([node.module] if node.module else []))
                else:
                    base = node.module or ''
                for a in node.names:
                    fullname = '.'.join(filter(None, [base, a.name]))
                    target = fullname if resolve(fullname) else base
                    targets.append((target, fullname))
                    aliases[a.asname or a.name] = fullname
            for target, imported in targets:
                dest = resolve(target)
                if dest and dest != path:
                    edges.append({'source': path, 'target': dest, 'line': node.lineno, 'import': imported, 'kind': 'import'})
            if isinstance(node, ast.Call):
                f = node.func
                chunks = []
                while isinstance(f, ast.Attribute):
                    chunks.insert(0, f.attr); f = f.value
                if isinstance(f, ast.Name):
                    chunks.insert(0, f.id)
                    if f.id in {'__import__', 'eval', 'exec', 'getattr'} or chunks == ['importlib', 'import_module']:
                        dynamic.append({'path': path, 'line': node.lineno, 'expression': '.'.join(chunks)})
                    full = '.'.join([aliases.get(chunks[0], current + '.' + chunks[0])] + chunks[1:])
                    target = None
                    for size in range(len(full.split('.')) - 1, 0, -1):
                        target = resolve('.'.join(full.split('.')[:size]))
                        if target:
                            break
                    if target:
                        calls.append({'source': path, 'target': target, 'line': node.lineno, 'symbol': full, 'confidence': 'syntactic-candidate'})
    seeds = sorted(set(changed))
    if symbol:
        # A matching symbol narrows the starting files, never claims exact runtime callers.
        seeds = [p for p in seeds if any(s['path'] == p and s['name'] == symbol for s in symbols)]
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
            'dynamic_sites': dynamic, 'warnings': warnings,
            'limits': 'Import reachability and syntactic calls only. No runtime dispatch, coverage proof or guarantee of breakage. Calls may be shadowed; external imports and plugins are not resolved.'}
