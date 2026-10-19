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

    def report(self, name, hits, filename="src/Lab.Application/Sites/SiteService.cs", assembly=None):
        directory = self.root / "TestResults" / name
        directory.mkdir(parents=True)
        root = ET.Element("coverage")
        if assembly is None:
            assembly = next(part for part in filename.replace("\\", "/").split("/") if part.startswith("Lab."))
        package = ET.SubElement(ET.SubElement(root, "packages"), "package", name=assembly)
        classes = ET.SubElement(package, "classes")
        lines = ET.SubElement(ET.SubElement(classes, "class", filename=filename), "lines")
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

    def test_short_long_and_absolute_paths_merge_with_zero_hits(self):
        self.report("short", [2, 0, 0, 0, 0], "Validation/DomainRules.cs", "Lab.Domain")
        self.report("long", [0, 3, 0, 0, 0], "Lab.Domain/Validation/DomainRules.cs", "Lab.Domain")
        self.report("windows", [0, 0, 4, 0, 0], r"C:\repo\src\Lab.Domain\Validation\DomainRules.cs", "Lab.Domain")
        self.report("unix", [0, 0, 0, 5, 0], "/repo/src/Lab.Domain/Validation/./DomainRules.cs", "Lab.Domain")
        result = self.run_summary()
        self.assertEqual(0, result.returncode)
        self.assertIn("Lab.Domain: 4/5 = 80.00 %", result.stdout)
        self.assertIn("РАЗОМ solution: 4/5", result.stdout)

    def test_same_path_in_different_assemblies_stays_separate(self):
        self.report("domain", [1], "Validation/Rules.cs", "Lab.Domain")
        self.report("application", [0], "Validation/Rules.cs", "Lab.Application")
        result = self.run_summary()
        self.assertEqual(1, result.returncode)
        self.assertIn("Lab.Domain: 1/1", result.stdout)
        self.assertIn("Lab.Application: 0/1", result.stdout)
        self.assertIn("1/2 = 50.00 %", result.stdout)

    def test_package_is_authoritative_not_path_substring(self):
        self.report("core", [0], "Rules.cs", "Lab.Domain")
        self.report("fake", [1] * 100, "Lab.Domain/Fake.cs", "Other.Lab.Domain")
        result = self.run_summary()
        self.assertEqual(1, result.returncode)
        self.assertIn("Lab.Domain: 0/1", result.stdout)
        self.assertIn("РАЗОМ solution: 100/101", result.stdout)

    def test_max_hits_is_preserved(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location("coverage_summary", SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.report("one", [7, 0], "Validation/DomainRules.cs", "Lab.Domain")
        self.report("two", [3, 0], "Lab.Domain/Validation/DomainRules.cs", "Lab.Domain")
        module.RESULTS_DIR = str(self.root / "TestResults")
        hits, count = module.collect_hits()
        self.assertEqual(2, count)
        self.assertEqual({("Lab.Domain", "Validation/DomainRules.cs", 1): 7,
                          ("Lab.Domain", "Validation/DomainRules.cs", 2): 0}, hits)

    def test_source_directory_resolves_absolute_path_without_assembly_segment(self):
        self.report("absolute", [1], r"C:\work\domain\Validation\Rules.cs", "Lab.Domain")
        path = self.root / "TestResults/absolute/coverage.cobertura.xml"
        tree = ET.parse(path)
        ET.SubElement(ET.SubElement(tree.getroot(), "sources"), "source").text = "C:/work/domain/"
        tree.write(path, encoding="utf-8")
        self.report("relative", [0], "Validation/Rules.cs", "Lab.Domain")
        result = self.run_summary()
        self.assertEqual(0, result.returncode)
        self.assertIn("Lab.Domain: 1/1", result.stdout)

    def test_unidentified_assembly_fails_instead_of_dropping_lines(self):
        self.report("valid", [1], "Site.cs", "Lab.Domain")
        path = self.root / "TestResults/valid/coverage.cobertura.xml"
        tree = ET.parse(path)
        tree.getroot().append(ET.Element("class", filename="Unknown.cs"))
        tree.write(path, encoding="utf-8")
        self.assertEqual(1, self.run_summary().returncode)


if __name__ == "__main__":
    unittest.main()
