import unittest
from forgescope.impact import analyze

class ImpactTests(unittest.TestCase):
    def test_transitive_chain_and_test_candidates(self):
        f={'src/pkg/a.py':'def f(): pass','src/pkg/b.py':'from .a import f\nf()',
           'tests/test_b.py':'from pkg.b import f','other.py':'pass'}
        r=analyze(f,['src/pkg/a.py'])
        self.assertEqual(r['test_candidates'],['tests/test_b.py'])
        self.assertEqual(len(r['affected'][-1]['chain']),2)
        self.assertEqual(r['calls'][0]['symbol'],'pkg.a.f')
        self.assertNotIn('other.py',[x['path'] for x in r['affected']])
    def test_import_module_alias(self):
        r=analyze({'pkg/a.py':'def f():pass','b.py':'import pkg.a as thing\nthing.f()'},['pkg/a.py'])
        self.assertEqual(r['calls'][0]['target'],'pkg/a.py')
    def test_import_from_package_submodule(self):
        r=analyze({'pkg/__init__.py':'','pkg/a.py':'','b.py':'from pkg import a'},['pkg/a.py'])
        self.assertEqual(len(r['affected']),2)
    def test_deleted_file_can_seed_existing_importers(self):
        # The known removed module path can still resolve existing imports.
        r=analyze({'b.py':'import gone'},['gone.py'])
        self.assertEqual(len(r['affected']),2)
        self.assertTrue(r['warnings'])
    def test_cycle_terminates(self):
        r=analyze({'a.py':'import b','b.py':'import a'},['a.py'])
        self.assertEqual(len(r['affected']),2)
    def test_parse_failure_is_visible(self):
        r=analyze({'broken.py':'def ('})
        self.assertEqual(r['warnings'][0]['reason'],'unparsed Python')
    def test_ambiguity_not_guessed(self):
        r=analyze({'src/a.py':'','a.py':'','b.py':'import a'},['a.py'])
        self.assertEqual(r['edges'],[])
        self.assertTrue(r['warnings'])
    def test_dynamic_sites_visible(self):
        r=analyze({'a.py':'__import__(name)\ngetattr(obj, name)()'})
        self.assertEqual(len(r['dynamic_sites']),2)
    def test_symbol_not_found(self):
        r=analyze({'a.py':'def x():pass'},['a.py'],'missing')
        self.assertFalse(r['changed'])
        self.assertTrue(r['warnings'])
