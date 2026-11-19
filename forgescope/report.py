import hashlib
import html
import json
import re
import sqlite3
import subprocess
from contextlib import closing
from pathlib import Path
from .impact import inventory, analyze

HERE = Path(__file__).parent

def redact(value):
    value = re.sub(r'\x1b\[[0-?]*[ -/]*[@-~]', '', value)
    value = re.sub(r'\b(?:gh[pousr]_[\w]+|github_pat_[\w]+|AKIA[A-Z0-9]{16})\b', '[REDACTED]', value)
    value = re.sub(r'(?i)(Bearer\s+)[\w.~/+\-=]+', r'\1[REDACTED]', value)
    value = re.sub(r'(?i)((?:password|secret|token|api_key)\s*[=:]\s*)[^\s,;]+', r'\1[REDACTED]', value)
    value = re.sub(r'(https?://)[^\s/@]+:[^\s/@]+@', r'\1[REDACTED]@', value)
    return value

def bounded(path, limit=2000000):
    path = Path(path)
    if path.stat().st_size > limit:
        raise ValueError(f'Input too large (limit {limit} bytes): {path.name}')
    return path.read_text(encoding='utf-8')

def normalize_path(value):
    value = value.replace('\\', '/')
    if value.startswith('./'):
        value = value[2:]
    if value.startswith('/') or re.match(r'^[A-Za-z]:', value) or '..' in value.split('/'):
        raise ValueError('Changed paths must be repository-relative without ..')
    return value

def make_report(root, changed=(), symbol=None, diff='', logs=()):
    files, skipped = inventory(root)
    if len(diff.encode()) > 2000000:
        raise ValueError('Diff exceeds 2 MB')
    if sum(len(log['text'].encode()) for log in logs) > 2000000 or len(logs) > 20:
        raise ValueError('Logs exceed 20 files / 2 MB total')
    changed = sorted({normalize_path(p) for p in changed})
    payload = {'files': files, 'changed': changed, 'diff': diff, 'logs': logs}
    # The bridge executes only bundled engines, never code/config from the scan target.
    try:
        run = subprocess.run(['node', str(HERE/'bridge.mjs')], input=json.dumps(payload),
                             text=True, encoding='utf-8', capture_output=True, timeout=45, check=True)
        engines = json.loads(run.stdout)
    except FileNotFoundError:
        engines = {n:{'status':'unavailable','error':'Node.js 22+ required for bundled upstream engines'}
                   for n in ['patchscope','cifuse','reporelief','devrescue']}
    except (subprocess.SubprocessError, json.JSONDecodeError) as exc:
        engines = {n:{'status':'error','error':type(exc).__name__}
                   for n in ['patchscope','cifuse','reporelief','devrescue']}
    patch=engines.get('patchscope',{}).get('report',{})
    changed=sorted(set(changed)|{normalize_path(f['path']) for f in patch.get('files',[]) if f.get('path')})
    impact = analyze(files, changed, symbol)
    findings = []
    def add(engine, rule, title, priority, path, line, evidence, action):
        identity = json.dumps([engine,rule,path,line,evidence], sort_keys=True)
        findings.append({'id':hashlib.sha256(identity.encode()).hexdigest()[:20], 'engine':engine,
                         'rule':rule,'title':title,'priority':priority,'path':path,'line':line,
                         'evidence':redact(str(evidence)),'action':redact(str(action))})
    for name, result in engines.items():
        if result['status'] != 'ok':
            continue
        for f in result['report'].get('findings', []):
            if name == 'patchscope':
                add(name,f['rule'],f['title'],f['severity'],f.get('path'),f.get('line'),f['reason'],f['action'])
            elif name == 'reporelief':
                e = f.get('evidence', [])
                path = next((p for p in files if any(v == p or v.startswith(p+':') for v in e)),None)
                add(name,f['id'],f['title'],f['priority'],path,None,'; '.join(e),f['action'])
            elif name == 'cifuse':
                add(name,f['rule'],f['title'],'medium',None,None,f['evidence'],f['hint'])
            else:
                loc=f.get('location') or {}
                path=loc.get('file','').replace('\\','/')
                # Accept exact or unique suffix matches; never guess between multiple matches.
                candidates=[p for p in files if path == p or (path and path.endswith('/'+p))]
                source=candidates[0] if len(candidates)==1 else None
                add(name,f['id'],f['title'],'medium',source,loc.get('line'),f['evidence'],'; '.join(f['steps']))
    affected = {a['path']:a for a in impact['affected']}
    for finding in findings:
        hit=affected.get(finding['path'])
        finding['correlation'] = {'relation':'same file in import impact set','distance':hit['distance']} if hit else None
    findings.sort(key=lambda f:(not bool(f['correlation']), {'high':0,'error':0,'medium':1,'low':2}.get(f['priority'],2),f['id']))
    tools=json.loads((HERE/'vendor/revscope/tools.json').read_text(encoding='utf-8'))
    # Metadata-based suggestions only. No binaries are executed or decompiled.
    suffixes={Path(p).suffix.lower() for p in files}
    ids={'die','ghidra'} if any(s['reason']=='binary' for s in skipped) else set()
    if suffixes & {'.pcap','.har'}: ids.update({'wireshark','mitmproxy'})
    if any(p.endswith(('.bin','.hex','.elf')) for p in files) or any(s['path'].endswith(('.bin','.hex','.elf')) for s in skipped):
        ids.update({'binwalk','qemu'})
    report={'schema_version':1,'project':Path(root).resolve().name,'inventory':{'text_files':len(files),'skipped':skipped},
            'impact':impact,'engines':{n:{k:v for k,v in r.items() if k!='report'} for n,r in engines.items()},
            'findings':findings,'tools':[t for t in tools if t['id'] in ids],
            'provenance':json.loads((HERE/'vendor/provenance.json').read_text(encoding='utf-8')),
            'limitations':['Correlations are shared file evidence, not proof of root cause.',
                           'Test candidates are import-reachable, not measured coverage.',
                           'Redaction is best effort; inspect reports before sharing.',
                           'RevScope recommends tools only; no reverse-engineering engine is included.']}
    # Redact every exported string, including nested filenames and context.
    def clean(value):
        if isinstance(value,str): return redact(value)
        if isinstance(value,list): return [clean(x) for x in value]
        if isinstance(value,dict): return {k:clean(v) for k,v in value.items()}
        return value
    return clean(report)

def baseline(report, previous):
    if previous.get('schema_version') != 1 or not isinstance(previous.get('findings'), list):
        raise ValueError('Expected ForgeScope schema_version 1 baseline')
    old={f['id'] for f in previous['findings']}
    current={f['id'] for f in report['findings']}
    for f in report['findings']:
        f['status']='existing' if f['id'] in old else 'new'
    report['comparison']={'new':len(current-old),'absent':len(old-current),'existing':len(current&old),
                          'note':'Absent does not establish that a defect was repaired.'}

def markdown(report):
    safe=lambda s:html.escape(str(s)).replace('`','').replace('\n',' ')
    rows=['# ForgeScope investigation','',f"Project: {safe(report['project'])}",'',
          f"{report['impact']['modules']} Python modules · {len(report['impact']['affected'])} affected files · {len(report['findings'])} findings",'',
          '## Import-reachable test candidates','']
    rows.extend('- '+safe(p) for p in report['impact']['test_candidates'])
    if not report['impact']['test_candidates']:rows.append('No import-reachable candidates found; this is not evidence of no impact.')
    rows.extend(['','## Evidence and actions',''])
    for f in report['findings']:
        rows.extend([f"### {safe(f['title'])}",f"{safe(f['engine'])} · {safe(f['path'] or 'no verified source location')} · {safe(f['priority'])}",safe(f['evidence']),safe(f['action']),''])
    rows.extend(['## Limits','']+['- '+s for s in report['limitations']])
    return '\n'.join(rows)

def save(report, directory):
    out=Path(directory);out.mkdir(parents=True,exist_ok=True)
    data=json.dumps(report,indent=2,ensure_ascii=False)
    (out/'report.json').write_text(data,encoding='utf-8')
    (out/'report.md').write_text(markdown(report),encoding='utf-8')
    template=(HERE/'viewer.html').read_text(encoding='utf-8')
    embedded=data.replace('<','\\u003c').replace('>','\\u003e').replace('&','\\u0026')
    (out/'index.html').write_text(template.replace('/*REPORT_DATA*/null',embedded),encoding='utf-8')
    return out

def record_history(report, database):
    path=Path(database);path.parent.mkdir(parents=True,exist_ok=True)
    with closing(sqlite3.connect(path)) as db:
        with db:
            db.execute('CREATE TABLE IF NOT EXISTS runs (id INTEGER PRIMARY KEY, created_at TEXT DEFAULT CURRENT_TIMESTAMP, project TEXT, report TEXT)')
            db.execute('INSERT INTO runs(project,report) VALUES (?,?)',(report['project'],json.dumps(report)))
