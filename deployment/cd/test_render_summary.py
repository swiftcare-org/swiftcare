import json
import tempfile
import unittest
from pathlib import Path

import render_summary as summary

NEW = "b" * 40
SELECTION = {
    "release_sha": NEW,
    "mode": "changed",
    "rows": [
        {"key": "auth", "previous": "aaaaaaaaaaaa", "decision": "Release", "reason": "3 file(s) changed"},
        {"key": "queue", "previous": "aaaaaaaaaaaa", "decision": "Release", "reason": "2 file(s) changed"},
        {"key": "patient", "previous": "aaaaaaaaaaaa", "decision": "Skip", "reason": "no changes"},
        {"key": "gateway", "previous": "aaaaaaaaaaaa", "decision": "Release", "reason": "shared file"},
    ],
}


def result(key, status, detail="", previous="a" * 40):
    return {"key": key, "status": status, "detail": detail, "previous": previous, "target": NEW}


class RenderTests(unittest.TestCase):
    def test_each_outcome_is_reported_per_deployable(self):
        results = {
            "auth": result("auth", "Deployed"),
            "queue": result("queue", "Rolled back", "Back on aaaaaaaaaaaa."),
        }
        text = summary.render(SELECTION, results)
        self.assertIn("| auth | `aaaaaaaaaaaa` | `bbbbbbbbbbbb` | Deployed |", text)
        self.assertIn("| queue | `aaaaaaaaaaaa` | see detail | Rolled back | Back on aaaaaaaaaaaa. |", text)
        self.assertIn("| patient | `aaaaaaaaaaaa` | unchanged | Skipped | no changes |", text)
        self.assertIn("| gateway | `aaaaaaaaaaaa` | unchanged | Not run |", text)
        self.assertIn("stay Running", text)

    def test_plan_only_changes_nothing_and_says_so(self):
        text = summary.render(SELECTION, {}, plan_only=True)
        self.assertIn("| auth | `aaaaaaaaaaaa` | - | Planned |", text)
        self.assertIn("nothing was changed in Azure", text)
        self.assertNotIn("stay Running", text)

    def test_first_deployment_without_a_previous_commit_uses_the_selection_value(self):
        text = summary.render(SELECTION, {"auth": result("auth", "Deployed", previous="")})
        self.assertIn("| auth | `aaaaaaaaaaaa` | `bbbbbbbbbbbb` | Deployed |", text)

    def test_results_are_read_from_a_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "auth.json").write_text(json.dumps(result("auth", "Deployed")), encoding="utf-8")
            self.assertEqual(set(summary.load_results(directory)), {"auth"})
        self.assertEqual(summary.load_results("does-not-exist"), {})
        self.assertEqual(summary.load_results(None), {})


if __name__ == "__main__":
    unittest.main()
