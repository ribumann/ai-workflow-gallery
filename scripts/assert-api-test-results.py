from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def assert_executed_tests(directory):
    reports = list(Path(directory).glob("*.trx"))
    if not reports:
        raise ValueError("No API test result reports were produced.")
    executed = 0
    for report in reports:
        counters = ET.parse(report).find(".//{*}Counters")
        if counters is None:
            raise ValueError(f"Missing test counters in {report.name}.")
        if int(counters.attrib["failed"]) != 0:
            raise ValueError(f"Failed API tests in {report.name}.")
        executed += int(counters.attrib["executed"])
    if executed <= 0:
        raise ValueError("The API test run executed zero tests.")
    return executed


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python assert-api-test-results.py <fresh-results-directory>")
    count = assert_executed_tests(sys.argv[1])
    print(f"Verified {count} executed API tests.")
