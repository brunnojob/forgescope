"""Explicit validation of a user-supplied patch in a disposable Git worktree."""
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

def validate_patch(repository, patch, command, timeout=60):
    if not isinstance(command,list) or not command or any(not isinstance(s,str) or not s for s in command):
        raise ValueError('Test command must be a non-empty JSON array of strings')
    root=Path(repository).resolve();patch=Path(patch).resolve()
    if not patch.is_file() or patch.stat().st_size>2000000:
        raise ValueError('Patch must be an existing file under 2 MB')
    def git(*args):
        return subprocess.run(['git','-c','core.hooksPath=/dev/null','-C',str(root),*args],capture_output=True,text=True,timeout=30,check=True)
    if git('status','--porcelain').stdout.strip():
        raise ValueError('Patch validation requires a clean committed repository')
    temp=Path(tempfile.mkdtemp(prefix='forgescope-'));work=temp/'checkout'
    added=False
    try:
        git('worktree','add','--detach',str(work),'HEAD');added=True
        def test():
            # No shell. Only explicit user-chosen argv. This is isolation of edits, not a security sandbox.
            with tempfile.TemporaryFile() as output:
                try:
                    r=subprocess.run(command,cwd=work,stdout=output,stderr=output,timeout=timeout)
                    return {'exit_code':r.returncode,'timed_out':False}
                except subprocess.TimeoutExpired:
                    return {'exit_code':None,'timed_out':True}
        before=test()
        # Discard build/test artifacts from the disposable worktree so stale bytecode
        # or generated files cannot make the after-run reuse the before result.
        subprocess.run(['git','-C',str(work),'reset','--hard','HEAD'],capture_output=True,check=True,timeout=30)
        subprocess.run(['git','-C',str(work),'clean','-fdx'],capture_output=True,check=True,timeout=30)
        subprocess.run(['git','-C',str(work),'apply','--check',str(patch)],capture_output=True,check=True,timeout=30)
        subprocess.run(['git','-C',str(work),'apply',str(patch)],capture_output=True,check=True,timeout=30)
        after=test()
        return {'before':before,'after':after,'validated':before['exit_code'] not in {0,None} and after['exit_code']==0,
                'scope':'Only the supplied test command; not proof of a general fix. Original checkout unchanged.'}
    finally:
        if added:git('worktree','remove','--force',str(work))
        shutil.rmtree(temp,ignore_errors=True)

def main(argv=None):
    import argparse
    p=argparse.ArgumentParser(description='Run trusted project tests before/after a supplied patch in a disposable worktree. Not a security sandbox.')
    p.add_argument('repository');p.add_argument('patch')
    p.add_argument('--test-command',required=True,help='Explicit JSON argv, e.g. ["python","-m","unittest","discover"]')
    p.add_argument('--timeout',type=int,default=60)
    p.add_argument('--output',default='validation.json')
    a=p.parse_args(argv)
    if not 1<=a.timeout<=300:p.error('timeout must be 1–300 seconds')
    try:
        result=validate_patch(a.repository,a.patch,json.loads(a.test_command),a.timeout)
        Path(a.output).write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(json.dumps(result,indent=2));return 0 if result['validated'] else 1
    except (OSError,ValueError,subprocess.SubprocessError) as exc:
        print(type(exc).__name__+': validation did not complete');return 2

if __name__=='__main__':raise SystemExit(main())
