import argparse
import json
import sys
from pathlib import Path
from .report import make_report, save, bounded, baseline, record_history

def main(argv=None):
    p=argparse.ArgumentParser(description='ForgeScope — evidence before edits. Local analysis; target code is never imported.')
    p.add_argument('repository', nargs='?', default='.')
    p.add_argument('--changed', action='append', default=[], help='Repository-relative file (repeatable; deleted files accepted)')
    p.add_argument('--symbol', help='Narrow changed-file seeds to a named definition; impact remains file-level')
    p.add_argument('--diff', help='Existing unified diff file; never runs Git hooks')
    p.add_argument('--log', action='append',default=[],help='Existing log file (repeatable)')
    p.add_argument('--baseline',help='Previous report.json for stable finding comparison')
    p.add_argument('--history',help='Opt-in local SQLite report history')
    p.add_argument('--output',default='.forgescope',help='Report directory (default: .forgescope)')
    p.add_argument('--fail-on',choices=['never','high','engine-error'],default='never')
    args=p.parse_args(argv)
    try:
        logs=[{'name':Path(x).name,'text':bounded(x)} for x in args.log]
        report=make_report(args.repository,args.changed,args.symbol,bounded(args.diff) if args.diff else '',logs)
        if args.baseline: baseline(report,json.loads(bounded(args.baseline,20000000)))
        if args.history: record_history(report,args.history)
        out=save(report,args.output)
        print(json.dumps({'report':str(out/'index.html'),'findings':len(report['findings']),
                          'affected':len(report['impact']['affected']),'engines':report['engines']},indent=2))
        if args.fail_on=='high' and any(f['priority'] in {'high','error'} for f in report['findings']):return 1
        if args.fail_on=='engine-error' and any(e['status'] in {'error','unavailable'} for e in report['engines'].values()):return 2
        return 0
    except (OSError,ValueError,KeyError,TypeError) as exc:
        print('ForgeScope: '+str(exc),file=sys.stderr);return 2

if __name__=='__main__':sys.exit(main())
