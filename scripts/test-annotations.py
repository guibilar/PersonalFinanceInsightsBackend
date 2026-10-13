#!/usr/bin/env python3
"""Writes the test results of TRX files as GitHub Actions annotations.

Usage: python3 scripts/test-annotations.py <label> <results directory>

- One ::notice:: per test project: passed, failed, skipped.
- One ::error:: per failed test: the test name and the first lines of its message.
- GitHub shows at most 10 errors per step, so after 9 failed tests one more error gives the count of the others.

Task sessions cannot read the CI logs, but they can read annotations (Hestia T40).
A copy of scripts/test-annotations.py in guibilar/HestiaBackend (T26). Only the Python standard library.
"""

import glob
import os
import sys
import xml.etree.ElementTree as ET

MAX_ERRORS = 10
MESSAGE_LINES = 6
MESSAGE_CHARS = 1000


def escape_data(text):
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def escape_property(text):
    return escape_data(text).replace(":", "%3A").replace(",", "%2C")


def annotate(level, title, message):
    print(f"::{level} title={escape_property(title)}::{escape_data(message)}")


def local(tag):
    """The tag name without the TRX namespace."""
    return tag.rsplit("}", 1)[-1]


def children(element, name):
    return [child for child in element if local(child.tag) == name]


def first_lines(text):
    lines = [line.rstrip() for line in text.strip().splitlines() if line.strip()]
    shown = "\n".join(lines[:MESSAGE_LINES])
    if len(lines) > MESSAGE_LINES:
        shown += "\n…"
    return shown[:MESSAGE_CHARS]


def read_trx(path):
    """Returns (project name, list of (test name, outcome, message))."""
    root = ET.parse(path).getroot()

    # The test project: the assembly of the first test definition, else the file name.
    project = None
    for definitions in children(root, "TestDefinitions"):
        for test in children(definitions, "UnitTest"):
            storage = test.get("storage")
            if storage:
                project = os.path.splitext(os.path.basename(storage.replace("\\", "/")))[0]
                break
        if project:
            break
    if not project:
        project = os.path.splitext(os.path.basename(path))[0]

    results = []
    for section in children(root, "Results"):
        for result in section.iter():
            if local(result.tag) != "UnitTestResult":
                continue
            message = ""
            for element in result.iter():
                if local(element.tag) == "Message" and element.text:
                    message = element.text
                    break
            results.append((result.get("testName", "(no name)"), result.get("outcome", ""), message))
    return project, results


def main():
    if len(sys.argv) != 3:
        print("Usage: test-annotations.py <label> <results directory>", file=sys.stderr)
        return 2
    label, directory = sys.argv[1], sys.argv[2]

    files = sorted(glob.glob(os.path.join(directory, "**", "*.trx"), recursive=True))
    if not files:
        annotate("warning", f"Tests ({label})", f"No test results (TRX files) in {directory}.")
        return 0

    projects = {}
    for path in files:
        try:
            project, results = read_trx(path)
        except ET.ParseError as error:
            annotate("warning", f"Tests ({label})", f"Cannot read {os.path.basename(path)}: {error}")
            continue
        projects.setdefault(project, []).extend(results)

    failures = []
    for project in sorted(projects):
        passed = failed = skipped = 0
        for name, outcome, message in projects[project]:
            if outcome == "Passed":
                passed += 1
            elif outcome in ("NotExecuted", "Inconclusive", "Pending"):
                skipped += 1
            else:
                # Failed, Error, Timeout, Aborted, and any other outcome.
                failed += 1
                failures.append((project, name, outcome, message))
        annotate(
            "notice",
            f"Tests ({label}): {project}",
            f"{passed} passed, {failed} failed, {skipped} skipped",
        )

    shown = failures if len(failures) <= MAX_ERRORS else failures[: MAX_ERRORS - 1]
    for project, name, outcome, message in shown:
        text = first_lines(message) or f"Outcome: {outcome} (no message)"
        annotate("error", f"Failed ({label}): {name}", f"{project}\n{text}")
    if len(failures) > len(shown):
        annotate(
            "error",
            f"Tests ({label})",
            f"{len(failures) - len(shown)} more failed tests. GitHub shows at most {MAX_ERRORS} errors per step.",
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
