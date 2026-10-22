"""Catalog aliases must preserve the reviewed CAD, placement and material map."""
import json
from pathlib import Path
import unittest


class UpstreamCompatibilityTests(unittest.TestCase):
    def test_renamed_parts_reuse_reviewed_templates(self):
        root = Path(__file__).resolve().parents[2]
        library = root / 'Assets/StreamingAssets/StepExport/Library'
        entries = json.loads((library / 'index.json').read_text())['parts']
        for alias, original in {'RAILS-2x25': 'RAIL-2x25', 'RAILS-2x35': 'RAIL-2x35',
                                'OVTO': 'OVTO-Red side'}.items():
            with self.subTest(alias=alias):
                entry = dict(entries[alias])
                self.assertEqual(entry.pop('aliasOf'), original)
                self.assertEqual(entry, entries[original])
                self.assertTrue((library / entry['file']).is_file())
        self.assertIn('RAIL-Rail-24', entries)
        self.assertNotIn('RAILS-Rail-24', entries)


if __name__ == '__main__':
    unittest.main()
