import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from forgescope.validation import validate_patch

class ValidationTests(unittest.TestCase):
    def test_before_fail_after_pass_original_preserved(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)/'repo';root.mkdir()
            def git(*args):return subprocess.run(['git','-C',str(root),*args],check=True,capture_output=True)
            git('init');git('config','user.name','Test');git('config','user.email','test@example.com')
            (root/'value.py').write_text('value = 1\n');git('add','.');git('commit','-m','fixture')
            patch=Path(d)/'fix.patch';patch.write_text('diff --git a/value.py b/value.py\n--- a/value.py\n+++ b/value.py\n@@ -1 +1 @@\n-value = 1\n+value = 2\n')
            result=validate_patch(root,patch,[sys.executable,'-c','from value import value; assert value == 2'])
            self.assertTrue(result['validated'])
            self.assertEqual((root/'value.py').read_text(),'value = 1\n')
            self.assertEqual(git('status','--porcelain').stdout,b'')
    def test_reject_shell_string(self):
        with self.assertRaises(ValueError):validate_patch('.','missing.patch','python test.py')
