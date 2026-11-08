import importlib.util
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
import xml.etree.ElementTree as ET


spec = importlib.util.spec_from_file_location(
    "api_results", Path(__file__).with_name("assert-api-test-results.py")
)
api_results = importlib.util.module_from_spec(spec)
spec.loader.exec_module(api_results)


class ApiTestResultsTests(unittest.TestCase):
    def test_requires_an_actual_report(self):
        with TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "No API test result"):
                api_results.assert_executed_tests(directory)

    def test_requires_counters_nonzero_execution_and_no_failures(self):
        cases = [
            ('<TestRun/>', ValueError),
            ('<TestRun><Counters executed="0" failed="0"/></TestRun>', ValueError),
            ('<TestRun><Counters executed="1" failed="1"/></TestRun>', ValueError),
            ('<TestRun><Counters failed="0"/></TestRun>', KeyError),
            ('<TestRun><Counters executed="1"/></TestRun>', KeyError),
            ('<TestRun><Counters executed="invalid" failed="0"/></TestRun>', ValueError),
            ('<TestRun>', ET.ParseError),
        ]
        for xml, exception in cases:
            with self.subTest(xml=xml), TemporaryDirectory() as directory:
                Path(directory, "results.trx").write_text(xml, encoding="utf-8")
                with self.assertRaises(exception):
                    api_results.assert_executed_tests(directory)

    def test_accepts_namespaced_executed_results(self):
        with TemporaryDirectory() as directory:
            Path(directory, "results.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                '<ResultSummary><Counters executed="348" failed="0"/></ResultSummary></TestRun>',
                encoding="utf-8",
            )
            self.assertEqual(api_results.assert_executed_tests(directory), 348)


if __name__ == "__main__":
    unittest.main()
