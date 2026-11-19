import json
import subprocess
import tempfile
import unittest
import re
from unittest.mock import patch
from forgescope.__main__ import main
from pathlib import Path
from forgescope.git import git_diff
from forgescope.report import make_report,baseline,save
from forgescope.sarif import to_sarif

class GitTests(unittest.TestCase):
    def test_tracked_staged_and_worktree_changes(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)
            def git(*args):return subprocess.run(['git','-C',d,*args],check=True,capture_output=True)
            git('init');git('config','user.name','Test');git('config','user.email','test@example.com')
            (p/'a.py').write_text('value = 1\n');(p/'with space.py').write_text('value = 1\n')
            git('add','.');git('commit','-m','base')
            (p/'a.py').write_text('value = 2\n');git('add','a.py')
            (p/'with space.py').write_text('value = 3\n');(p/'untracked.py').write_text('pass')
            diff,rev,paths=git_diff(p,'HEAD')
            self.assertEqual(set(paths),{'a.py','with space.py'})
            self.assertIn('+value = 2',diff);self.assertIn('+value = 3',diff)
            self.assertEqual(len(rev),40)
            with self.assertRaises(ValueError):git_diff(p,'HEAD',limit=10)

    def test_revision_options_rejected(self):
        with self.assertRaises(ValueError):git_diff('.','--output=/tmp/anything')

    def test_invalid_revision_error(self):
        with self.assertRaises(ValueError):git_diff('.','not-a-real-revision')

    def test_cli_git_seeds_survive_missing_node(self):
        with tempfile.TemporaryDirectory() as d, tempfile.TemporaryDirectory() as out:
            p=Path(d)
            def git(*args):return subprocess.run(['git','-C',d,*args],check=True,capture_output=True)
            git('init');git('config','user.name','Test');git('config','user.email','test@example.com')
            (p/'a.py').write_text('value = 1\n');(p/'b.py').write_text('from a import value\n')
            git('add','.');git('commit','-m','fixture')
            (p/'a.py').write_text('value = 2\n')
            real_run=subprocess.run
            def run(argv,*args,**kwargs):
                if argv[0]=='node':raise FileNotFoundError('Node missing')
                return real_run(argv,*args,**kwargs)
            with patch('subprocess.run',side_effect=run):
                self.assertEqual(main([d,'--base','HEAD','--output',out]),0)
            report=json.loads((Path(out)/'report.json').read_text(encoding='utf-8'))
            self.assertEqual({a['path'] for a in report['impact']['affected']},{'a.py','b.py'})
            self.assertEqual(report['engines']['patchscope']['status'],'unavailable')

class SarifTests(unittest.TestCase):
    def report(self):
        return {'findings':[{'id':'abc','engine':'impact','rule':'missing-local-export','title':'Review import',
                             'priority':'medium','path':'src/with space.py','line':3,'evidence':'missing value',
                             'action':'Review exports','status':'new'}]}

    def test_uri_encoding_location_and_baseline(self):
        d=to_sarif(self.report());r=d['runs'][0]['results'][0]
        self.assertEqual(d['version'],'2.1.0')
        self.assertEqual(r['locations'][0]['physicalLocation']['artifactLocation']['uri'],'src/with%20space.py')
        self.assertEqual(r['locations'][0]['physicalLocation']['region']['startLine'],3)
        self.assertEqual(r['baselineState'],'new')

    def test_no_location_when_unknown(self):
        d=self.report();d['findings'][0]['path']=None
        self.assertNotIn('locations',to_sarif(d)['runs'][0]['results'][0])

    def test_browser_sarif_matches_python(self):
        template=(Path(__file__).resolve().parents[1]/'forgescope/viewer.html').read_text(encoding='utf-8')
        script=re.findall(r'<script>(.*?)</script>',template,re.S)[0].replace('if(report)render();','')
        data=self.report()
        prelude='const nodes=new Map(); const document={getElementById(id){if(!nodes.has(id))nodes.set(id,{textContent:id==="report-data"?'+json.dumps(json.dumps(data))+':""});return nodes.get(id)}};\n'
        run=subprocess.run(['node','--input-type=commonjs'],input=prelude+script+'\nconsole.log(JSON.stringify(sarifData()));',text=True,encoding='utf-8',capture_output=True,check=True)
        self.assertEqual(json.loads(run.stdout),to_sarif(data))

    def test_baseline_malformed_entries_are_rejected(self):
        for previous in [[],{'schema_version':1,'findings':[None]},{'schema_version':1,'findings':[{'id':1}]}]:
            with self.assertRaises(ValueError):baseline(self.report(),previous)

    def test_native_import_evidence_and_sarif_saved(self):
        with tempfile.TemporaryDirectory() as d, tempfile.TemporaryDirectory() as out:
            p=Path(d);(p/'a.py').write_text('def renamed():pass');(p/'b.py').write_text('from a import old_name')
            r=make_report(p,['a.py']);finding=next(f for f in r['findings'] if f['engine']=='impact')
            self.assertEqual(finding['path'],'b.py');self.assertTrue(finding['correlation'])
            save(r,out);self.assertEqual(json.loads((Path(out)/'report.sarif').read_text(encoding='utf-8'))['version'],'2.1.0')
