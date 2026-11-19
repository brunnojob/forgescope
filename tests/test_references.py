import unittest
from forgescope.impact import analyze

class ReferenceTests(unittest.TestCase):
    def source(self,text):
        return analyze({'a.py':'def run(): pass\n','b.py':text},['a.py'])

    def test_parameter_shadows_import(self):
        r=self.source('from a import run\ndef caller(run):\n    run()\n')
        self.assertEqual(r['calls'],[])

    def test_assignment_shadows_import(self):
        r=self.source('from a import run\ndef caller():\n    run()\n    run = 1\n')
        self.assertEqual(r['calls'],[])

    def test_inner_imports_do_not_leak(self):
        r=self.source('def first():\n    from a import run\n    run()\ndef second():\n    run()\n')
        self.assertEqual(len(r['calls']),1)
        self.assertEqual(r['calls'][0]['caller'],'b.first')

    def test_class_namespace_does_not_close_over_method(self):
        r=self.source('class Thing:\n    from a import run\n    def method(self):\n        run()\n')
        self.assertEqual(r['calls'],[])

    def test_closure_preserves_import(self):
        r=self.source('def outer():\n    from a import run\n    def inner():\n        run()\n')
        self.assertEqual(r['calls'][0]['caller'],'b.outer.inner')

    def test_comprehension_local_binding(self):
        r=self.source('from a import run\nvalues = [run() for run in callbacks]\nrun()')
        self.assertEqual(len(r['calls']),1)
        self.assertEqual(r['calls'][0]['line'],3)

    def test_lambda_parameter_binding(self):
        r=self.source('from a import run\nf = lambda run: run()')
        self.assertFalse(r['calls'])

    def test_builtin_call_not_invented_as_local(self):
        self.assertFalse(analyze({'a.py':'len([])\nprint("hi")'})['calls'])

    def test_missing_local_export_is_review_lead(self):
        r=self.source('from a import removed')
        self.assertEqual(r['unresolved_references'][0]['symbol'],'a.removed')
        self.assertEqual(r['unresolved_references'][0]['confidence'],'review-required')

    def test_reexport_and_variable_are_known_bindings(self):
        r=analyze({'a.py':'from external import value\ncount = 1',
                   'b.py':'from a import value, count'})
        self.assertFalse(r['unresolved_references'])

    def test_dynamic_exports_not_claimed_missing(self):
        for a in ['def __getattr__(name): return 1','from external import *']:
            self.assertFalse(analyze({'a.py':a,'b.py':'from a import generated'})['unresolved_references'])

    def test_external_modules_not_claimed_missing(self):
        self.assertFalse(analyze({'b.py':'from external import anything'})['unresolved_references'])

    def test_qualified_symbol_selection(self):
        r=analyze({'a.py':'class Thing:\n    def run(self):pass'},['a.py'],'a.Thing.run')
        self.assertEqual(r['changed'],['a.py'])

    def test_package_initializer_impact(self):
        r=analyze({'pkg/__init__.py':'','pkg/a.py':'def run():pass','client.py':'from pkg.a import run'},['pkg/__init__.py'])
        self.assertEqual({a['path'] for a in r['affected']},{'pkg/__init__.py','pkg/a.py','client.py'})

    def test_invalid_relative_import_not_resolved_as_absolute(self):
        r=analyze({'a.py':'','pkg/b.py':'from ...a import run'},['a.py'])
        self.assertFalse(r['edges']);self.assertTrue(r['warnings'])

    def test_dynamic_import_alias_recognized(self):
        r=analyze({'a.py':'from importlib import import_module as load\nload(name)'})
        self.assertEqual(len(r['dynamic_sites']),1)
