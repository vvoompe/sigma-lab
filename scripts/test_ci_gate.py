"""Перевірки gate через CLI, включно з падінням, скасуванням і штатним skip."""

import json
import os
from pathlib import Path
import subprocess
import sys
import unittest


class CiGateTests(unittest.TestCase):
    def run_gate(self, relevant="true", maui="success", failed_job=None, status="failure"):
        needs = {job: {"result": "success"} for job in
                 ("changes", "build-and-test", "server-sdk-compatibility")}
        needs["changes"]["outputs"] = {"maui": relevant}
        needs["maui-build"] = {"result": maui}
        if failed_job:
            needs[failed_job]["result"] = status
        return self.invoke(json.dumps(needs))

    def invoke(self, data):
        return subprocess.run(
            [sys.executable, str(Path(__file__).with_name("ci-gate.py"))],
            env={**os.environ, "NEEDS_JSON": data, "PYTHONIOENCODING": "utf-8"},
            capture_output=True, text=True, encoding="utf-8", check=False,
        ).returncode

    def test_successful_relevant_maui_passes(self):
        self.assertEqual(0, self.run_gate())

    def test_irrelevant_skip_passes(self):
        self.assertEqual(0, self.run_gate("false", "skipped"))

    def test_relevant_failure_skip_and_cancellation_fail(self):
        for status in ("failure", "skipped", "cancelled", "unknown"):
            with self.subTest(status=status):
                self.assertEqual(1, self.run_gate(maui=status))

    def test_irrelevant_failure_is_not_silently_accepted(self):
        for status in ("failure", "cancelled"):
            with self.subTest(status=status):
                self.assertEqual(1, self.run_gate("false", status))

    def test_server_and_path_filter_must_succeed(self):
        for job in ("changes", "build-and-test", "server-sdk-compatibility"):
            for status in ("failure", "cancelled", "skipped"):
                with self.subTest(job=job, status=status):
                    self.assertEqual(1, self.run_gate(failed_job=job, status=status))

    def test_unknown_path_filter_fails(self):
        self.assertEqual(1, self.run_gate(""))

    def test_missing_or_invalid_metadata_fails(self):
        for data in ("{}", "not-json", "null", "[]"):
            with self.subTest(data=data):
                self.assertEqual(1, self.invoke(data))


if __name__ == "__main__":
    unittest.main()
