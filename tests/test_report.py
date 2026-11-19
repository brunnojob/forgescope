import json
import sqlite3
import tempfile
import unittest
from contextlib import closing
from pathlib import Path
from forgescope.impact import inventory
from forgescope.report import make_report,save,baseline,record_history,normalize_path

ROOT=Path(__file__).resolve().parents[1]

class ReportTests(unittest.TestCase):
    def fixture(self):
        return make_report(ROOT/'examples/sample',['src/shop/pricing.py'],diff=(ROOT/'examples/change.diff').read_text(),logs=[{'name':'build.log','text':(ROOT/'examples/build.log').read_text()}])
    def test_real_engines_and_correlation(self):
        r=self.fixture()
        self.assertTrue(all(e['status']=='ok' for e in r['engines'].values()),r['engines'])
        self.assertEqual({f['engine'] for f in r['findings']},{'cifuse','devrescue','patchscope','reporelief'})
        self.assertTrue(any(f['correlation'] and f['engine']=='devrescue' for f in r['findings']))
        self.assertIn('tests/test_orders.py',r['impact']['test_candidates'])
    def test_baseline_stable(self):
        r=self.fixture();baseline(r,self.fixture())
        self.assertEqual(r['comparison']['new'],0)
        self.assertEqual(r['comparison']['absent'],0)
    def test_baseline_validation(self):
        with self.assertRaises(ValueError):baseline(self.fixture(),{})
    def test_report_html_escapes_script_end(self):
        r=self.fixture();r['project']='</script><img src=x onerror=alert(1)>'
        with tempfile.TemporaryDirectory() as d:
            save(r,d);text=(Path(d)/'index.html').read_text(encoding='utf-8')
            self.assertNotIn('</script><img',text)
            self.assertIn('\\u003c/script',text)
            self.assertEqual(json.loads((Path(d)/'report.json').read_text(encoding='utf-8'))['project'],r['project'])
    def test_inventory_excludes_secrets_and_symlinks(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'.env').write_text('SECRET=x');(p/'ok.py').write_text('x=1')
            try:(p/'link.py').symlink_to(p/'ok.py')
            except OSError:pass
            files,skipped=inventory(p)
            self.assertEqual(list(files),['ok.py'])
            self.assertTrue(skipped)
    def test_no_target_execution(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);marker=p/'executed'
            (p/'app.py').write_text('from pathlib import Path\nPath('+repr(str(marker))+').write_text("executed")\nraise RuntimeError("must never run")')
            make_report(p,['app.py']);self.assertFalse(marker.exists())
    def test_history(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'history.sqlite';record_history(self.fixture(),p)
            with closing(sqlite3.connect(p)) as db:self.assertEqual(db.execute('SELECT COUNT(*) FROM runs').fetchone()[0],1)
    def test_path_rejection(self):
        for path in ['../escape','/tmp/a','C:\\x.py']:
            with self.assertRaises(ValueError):normalize_path(path)
    def test_redaction_preserves_json_structure(self):
        r=make_report(ROOT/'examples/sample',logs=[{'name':'a','text':'ModuleNotFoundError: API_KEY=secret-value'}])
        self.assertNotIn('secret-value',json.dumps(r))
