"""Перевірки справжнього CLI порога покриття на ізольованих звітах Cobertura."""

from pathlib import Path
import os
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET


SCRIPT = Path(__file__).with_name("coverage-summary.py").resolve()


class CoverageSummaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def report(self, name, hits, filename="src/Lab.Application/Sites/SiteService.cs"):
        directory = self.root / "TestResults" / name
        directory.mkdir(parents=True)
        root = ET.Element("coverage")
        lines = ET.SubElement(ET.SubElement(root, "class", filename=filename), "lines")
        for number, hit in enumerate(hits, 1):
            ET.SubElement(lines, "line", number=str(number), hits=str(hit))
        ET.ElementTree(root).write(directory / "coverage.cobertura.xml", encoding="utf-8")

    def run_summary(self):
        return subprocess.run(
            [sys.executable, str(SCRIPT)], cwd=self.root,
            capture_output=True, text=True, encoding="utf-8", check=False,
            env={**os.environ, "PYTHONIOENCODING": "utf-8"},
        )

    def test_below_80_fails_and_keeps_artifact(self):
        self.report("one", [1] * 79 + [0] * 21)
        result = self.run_summary()
        self.assertEqual(1, result.returncode)
        self.assertIn("79/100 = 79.00 %", result.stdout)
        self.assertEqual(result.stdout, (self.root / "TestResults/coverage-summary.txt").read_text(encoding="utf-8"))

    def test_exactly_80_passes(self):
        self.report("one", [1] * 80 + [0] * 20)
        result = self.run_summary()
        self.assertEqual(0, result.returncode)
        self.assertIn("80/100 = 80.00 %", result.stdout)

    def test_reports_merge_by_line_without_double_counting(self):
        self.report("one", [1, 0, 1, 0, 0])
        self.report("two", [0, 1, 0, 1, 0])
        result = self.run_summary()
        self.assertEqual(0, result.returncode)
        self.assertIn("4/5 = 80.00 %", result.stdout)

    def test_windows_paths_are_recognized(self):
        self.report("one", [1], r"C:\repo\src\Lab.Domain\Sites\Site.cs")
        self.assertEqual(0, self.run_summary().returncode)

    def test_non_core_coverage_cannot_hide_uncovered_core(self):
        self.report("core", [0])
        self.report("api", [1] * 100, "src/Lab.Api/Controllers/SitesController.cs")
        self.assertEqual(1, self.run_summary().returncode)

    def test_missing_results_fail(self):
        self.assertEqual(1, self.run_summary().returncode)

    def test_empty_results_fail(self):
        (self.root / "TestResults").mkdir()
        self.assertEqual(1, self.run_summary().returncode)

    def test_missing_core_fails(self):
        self.report("one", [1], "src/Lab.Api/Program.cs")
        self.assertEqual(1, self.run_summary().returncode)


if __name__ == "__main__":
    unittest.main()
