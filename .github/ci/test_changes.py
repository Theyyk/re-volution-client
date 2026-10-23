import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('changes', Path(__file__).with_name('changes.py'))
changes = importlib.util.module_from_spec(spec)
spec.loader.exec_module(changes)
rules = json.loads(Path('.github/ci/components.json').read_text())


class ChangeTests(unittest.TestCase):
    def test_docs_skip_builds(self):
        self.assertFalse(any(changes.select(['README.md', 'docs/guide.md'], rules).values()))

    def test_workflow_changes_validate_components(self):
        self.assertTrue(all(changes.select(['.github/workflows/build.yml'], rules).values()))

    def test_unknown_diff_and_manual_build_everything(self):
        self.assertTrue(all(changes.select([], rules, force=True).values()))
        self.assertIsNone(changes.changed_paths('workflow_dispatch', {}, 'refs/heads/main'))
        self.assertIsNone(changes.changed_paths('push', {'before': '0'*40}, 'refs/heads/new'))
        self.assertIsNone(changes.changed_paths('push', {}, 'refs/tags/v1.3.7'))

    def test_unclassified_build_input_is_conservative(self):
        self.assertTrue(all(changes.select(['new-project/build.custom'], rules).values()))

    def test_component_inputs(self):
        cases = json.loads(Path('.github/ci/cases.json').read_text())
        for path, expected in cases.items():
            with self.subTest(path=path):
                self.assertEqual({k for k, v in changes.select([path], rules).items() if v}, set(expected))

    def test_complete_diff_keeps_deletions_renames_and_large_changes(self):
        payload = b'old/source.cs\0new/source.cs\0' + b'docs/a.md\0' * 400
        with patch.object(subprocess, 'check_output', return_value=payload) as run:
            paths = changes.changed_paths('push', {'before': 'a'*40, 'after': 'b'*40}, 'refs/heads/main')
            self.assertEqual(len(paths), 402)
            self.assertIn('--no-renames', run.call_args.args[0])
            self.assertIn('old/source.cs', paths)

    def test_pr_uses_merge_base(self):
        event = {'pull_request': {'base': {'sha': 'base'}, 'head': {'sha': 'head'}}}
        with patch.object(subprocess, 'check_output', side_effect=['ancestor', b'src/file.cs\0']) as run:
            self.assertEqual(changes.changed_paths('pull_request', event, 'refs/pull/1/merge'), ['src/file.cs'])
            self.assertEqual(run.call_args.args[0][-2:], ['ancestor', 'head'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
