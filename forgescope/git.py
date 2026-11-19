"""Bounded, read-only Git diff capture without shell or external diff drivers."""
import subprocess
import tempfile
from pathlib import Path

def git_diff(repository, base, limit=2000000):
    root=Path(repository).resolve()
    if not isinstance(base,str) or not base or base.startswith('-'):
        raise ValueError('Base must be a Git revision, not a command option')
    command=['git','--no-optional-locks','-c','core.hooksPath=/dev/null','-c','core.fsmonitor=false','-C',str(root)]
    try:
        rev=subprocess.run(command+['rev-parse','--verify','--end-of-options',base+'^{commit}'],
                           capture_output=True,text=True,encoding='utf-8',timeout=15,check=True).stdout.strip()
        # A temporary output file avoids buffering an arbitrarily large diff in memory.
        with tempfile.TemporaryFile() as out, tempfile.TemporaryFile() as err:
            result=subprocess.run(command+['diff','--no-ext-diff','--no-textconv','--no-color','--no-renames',rev,'--'],
                                  stdout=out,stderr=err,timeout=30)
            if result.returncode:raise ValueError('Git diff failed; verify the repository and base revision')
            if out.tell()>limit:raise ValueError('Git diff exceeds the 2 MB limit; narrow the change')
            out.seek(0)
            diff=out.read().decode('utf-8',errors='replace')
        with tempfile.TemporaryFile() as out, tempfile.TemporaryFile() as err:
            result=subprocess.run(command+['diff','--no-ext-diff','--no-textconv','--no-renames','--name-only','-z',rev,'--'],stdout=out,stderr=err,timeout=30)
            if result.returncode or out.tell()>limit:raise ValueError('Cannot read bounded changed-file inventory')
            out.seek(0)
            paths=[p.decode('utf-8',errors='replace') for p in out.read().split(b'\0') if p]
        return diff,rev,paths
    except (subprocess.SubprocessError,FileNotFoundError) as exc:
        raise ValueError('Cannot read the Git base revision; Git and a local commit are required') from exc
