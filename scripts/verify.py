"""One verification entry point; absent stacks are explicit, incomplete stacks fail."""

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

from check_repository import ROOT, elements, property_true, read_json, repository_files


class VerificationError(Exception):
    """A required verification condition was not met."""


def run(command, root, capture=False, environment_overrides=None):
    print("RUN: " + " ".join(str(item) for item in command), flush=True)
    environment = os.environ.copy()
    environment.update({"CI": "true", "DOTNET_CLI_UI_LANGUAGE": "en-US"})
    environment.update(environment_overrides or {})
    result = subprocess.run(command, cwd=root, env=environment, check=True,
                            text=True, capture_output=capture)
    return result.stdout if capture else ""


def require_tool(name):
    if not shutil.which(name):
        raise VerificationError(f"Required tool is unavailable: {name}")


def backend_inventory(root, paths):
    projects = sorted(path for path in paths if path.suffix == ".csproj")
    sources = [path for path in paths if path.suffix == ".cs" and path.parts[0] in {"src", "tests"}]
    if not projects:
        if sources:
            raise VerificationError("C# sources exist without a project; backend checks cannot be skipped")
        return None
    solutions = [path for path in paths if len(path.parts) == 1 and path.suffix in {".sln", ".slnx"}]
    if len(solutions) != 1:
        raise VerificationError("Backend projects require exactly one root .sln or .slnx")
    for source in sources:
        if not any(project.parent in source.parents for project in projects):
            raise VerificationError(f"C# source has no containing project: {source}")
    test_projects = []
    for project in projects:
        tree = ET.parse(root / project)
        if property_true(tree, "IsTestProject"):
            test_projects.append(project)
    if not test_projects:
        raise VerificationError("Backend code requires behavioral tests with explicit IsTestProject=true")
    return solutions[0], projects, test_projects


def require_test_results(directory):
    reports = list(directory.rglob("*.trx"))
    if not reports:
        raise VerificationError(f"No TRX report produced in {directory}")
    for report in reports:
        tree = ET.parse(report)
        counters = elements(tree, "Counters")
        summaries = elements(tree, "ResultSummary")
        if len(counters) != 1 or not summaries:
            raise VerificationError(f"Incomplete TRX report: {report}")
        values = counters[0].attrib
        if int(values.get("executed", "0")) == 0:
            raise VerificationError(f"Zero tests executed: {report}")
        if any(int(values.get(key, "0")) for key in ("failed", "error", "timeout", "aborted", "notExecuted")):
            raise VerificationError(f"Failed, aborted or skipped tests in {report}")
        if summaries[0].get("outcome") not in {"Completed", "Passed"}:
            raise VerificationError(f"Unsuccessful test run: {report}")


def prepare_browser_test(root, project):
    tree = ET.parse(root / project)
    if not property_true(tree, "IsBrowserTestProject"):
        return None
    require_tool("pwsh")
    output = run(["dotnet", "msbuild", str(project), "-property:Configuration=Release",
                  "-getProperty:TargetDir"], root, capture=True).strip()
    if not output:
        raise VerificationError(f"No Release output directory resolved for {project}")
    script = (root / output / "playwright.ps1").resolve()
    if not script.is_file():
        raise VerificationError(f"Built Playwright installer is missing: {script}")
    command = ["pwsh", "-NoProfile", "-File", str(script), "install"]
    # The non-root Docker image installs OS dependencies at build time. Browser launch
    # still runs in every suite and fails if the image is incomplete.
    if sys.platform.startswith("linux") and os.environ.get("BI_BROWSER_SYSTEM_DEPS") != "preinstalled":
        command.append("--with-deps")
    command.append("chromium")
    run(command, root)
    directory = root / ".artifacts" / "browser-tests" / project.with_suffix("")
    if directory.exists():
        shutil.rmtree(directory)
    directory.mkdir(parents=True)
    return {"BROWSER_ARTIFACTS_DIR": str(directory.resolve())}


def verify_backend(root, paths):
    inventory = backend_inventory(root, paths)
    if inventory is None:
        print("NOT APPLICABLE: backend build/tests (no application projects)")
        print("NOT APPLICABLE: browser tests (no application or browser test projects)")
        return
    solution, projects, test_projects = inventory
    require_tool("dotnet")
    expected = read_json(root / "global.json")["sdk"]["version"]
    actual = run(["dotnet", "--version"], root, capture=True).strip()
    if actual != expected:
        raise VerificationError(f"Expected .NET SDK {expected}, found {actual}")
    listed = run(["dotnet", "sln", str(solution), "list"], root, capture=True)
    included = {Path(line.strip().replace("\\", "/")) for line in listed.splitlines() if line.strip().endswith(".csproj")}
    if included != set(projects):
        raise VerificationError("Root solution project list differs from discovered projects")
    run(["dotnet", "restore", str(solution), "--locked-mode", "-warnaserror"], root)
    run(["dotnet", "build", str(solution), "--configuration", "Release", "--no-restore", "-warnaserror"], root)
    run(["dotnet", "format", str(solution), "--verify-no-changes", "--no-restore", "--severity", "warn"], root)
    run(["dotnet", "format", "style", str(solution), "--diagnostics", "IDE0005",
         "--verify-no-changes", "--no-restore", "--severity", "info"], root)
    has_browser_tests = False
    for project in test_projects:
        browser_environment = prepare_browser_test(root, project)
        has_browser_tests = has_browser_tests or browser_environment is not None
        directory = root / ".artifacts" / "test-results" / project.with_suffix("")
        if directory.exists():
            shutil.rmtree(directory)
        directory.mkdir(parents=True)
        run(["dotnet", "test", str(project), "--configuration", "Release", "--no-build", "--no-restore",
             "--logger", "trx;LogFileName=results.trx", "--results-directory", str(directory)], root,
            environment_overrides=browser_environment)
        require_test_results(directory)
    if not has_browser_tests:
        print("NOT APPLICABLE: browser tests (no marked browser test projects)")


def verify_frontend(root, paths):
    """The UI is Razor + CSS by decision (docs/engineering/design-system.md); no npm toolchain is verified."""
    packages = [path for path in paths if path.name == "package.json" and path.parts[0] in {"src", "tests"}]
    scripts = [path for path in paths if path.suffix in {".ts", ".tsx"} and path.parts[0] in {"src", "tests"}]
    if packages or scripts:
        raise VerificationError("Frontend packages/TypeScript are excluded by decision; record an ADR before adding them: "
                                + ", ".join(str(path) for path in packages + scripts))
    print("NOT APPLICABLE: frontend package checks (Razor + CSS, no frontend packages)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-only", action="store_true", help="Do not claim application verification")
    arguments = parser.parse_args()
    try:
        run([sys.executable, "scripts/check_repository.py"], ROOT)
        run([sys.executable, "-m", "unittest", "discover", "-s", "scripts/tests", "-v"], ROOT)
        if arguments.repository_only:
            print("NOT RUN: application verification (--repository-only explicitly requested)")
        else:
            paths = repository_files(ROOT)
            verify_frontend(ROOT, paths)
            verify_backend(ROOT, paths)
    except (VerificationError, OSError, ValueError, ET.ParseError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("PASS: all applicable verification completed; see explicit NOT RUN/NOT APPLICABLE entries")
    return 0


if __name__ == "__main__":
    sys.exit(main())
