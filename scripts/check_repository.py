"""Fast repository contracts. Standard library only; no network or application execution."""

import json
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
TEXT_SUFFIXES = {
    ".md", ".json", ".py", ".yml", ".yaml", ".props", ".targets", ".config",
    ".cs", ".csproj", ".slnx", ".ts", ".tsx", ".js", ".mjs", ".css", ".html",
}
REQUIRED_PATHS = (
    "AGENTS.md", "CONTRIBUTING.md", "README.md", ".editorconfig", ".gitattributes",
    "global.json", "Directory.Build.props", "Directory.Packages.props",
    "NuGet.Config", ".github/PULL_REQUEST_TEMPLATE.md", ".github/workflows/verify.yml",
    "docs/engineering/coding-standards.md", "docs/engineering/testing-and-verification.md",
    "docs/engineering/security-and-data.md", ".research/implementation-map.json",
    "scripts/branch_policy.py", "scripts/install_hooks.py", "docs/engineering/branch-policy.md",
    ".githooks/pre-commit", ".githooks/pre-merge-commit", ".githooks/pre-push",
    ".github/rulesets/protected-branches.json",
    "Dockerfile", "compose.yaml", ".dockerignore", "scripts/prepare_docker.py",
    "docs/engineering/docker-local.md",
)


def repository_files(root):
    result = subprocess.run(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=root, check=True, capture_output=True,
    )
    return sorted({Path(item.decode("utf-8")) for item in result.stdout.split(b"\0")
                   if item and (root / item.decode("utf-8")).is_file()})


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON key: " + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_object)


def markdown_targets(content):
    """Check inline file links, excluding fenced examples and external/anchor links."""
    fence = None
    for line in content.splitlines():
        marker = re.match(r"^\s*(`{3,}|~{3,})", line)
        if marker:
            current = marker.group(1)[0]
            fence = None if fence == current else (current if fence is None else fence)
            continue
        if fence:
            continue
        for match in re.finditer(r"!?\[[^\]]*\]\((<[^>]+>|[^\s)]+)(?:\s+\"[^\"]*\")?\)", line):
            target = match.group(1).strip("<>")
            if urlsplit(target).scheme or target.startswith(("#", "//")):
                continue
            target = unquote(target.split("#", 1)[0])
            if target:
                yield target


def check_text(path, content):
    errors = []
    if "\r" in content:
        errors.append(f"{path}: use LF line endings")
    # NuGet writes its generated lockfile without a final newline; preserve its output.
    if content and not content.endswith("\n") and path.name != "packages.lock.json":
        errors.append(f"{path}: missing final newline")
    for number, line in enumerate(content.splitlines(), 1):
        if line.rstrip() != line:
            errors.append(f"{path}:{number}: trailing whitespace")
        if re.search(r"-----BEGIN (?:[A-Z0-9]+ )?PRIVATE KEY-----", line):
            errors.append(f"{path}:{number}: private-key marker must not be committed")
    return errors


def check_credential_path(path):
    name = path.name.lower()
    example = name.endswith(".example")
    if ((name == ".env" or name.startswith(".env.")) and not example
            or name.endswith((".pfx", ".p12")) or name in {"id_rsa", "id_ed25519"}
            or name.startswith("appsettings.") and name.endswith(".local.json")):
        return [f"{path}: credential/local-settings file must not be committed"]
    return []


def inside_research(root, value):
    path = (root / ".research" / value).resolve()
    try:
        path.relative_to((root / ".research").resolve())
    except ValueError:
        return None
    return path


def check_map(data, root):
    """MVP steps (Sxx) with tasks (Sxx.n); a completed task needs its note, a completed step all tasks."""
    errors = []
    statuses = {"planned", "in_progress", "completed"}
    records = {}
    if data.get("version") != "mvp":
        errors.append("implementation-map: scope must remain mvp until a new plan is agreed")
    for item in data.get("steps", []):
        identifier = item.get("id", "")
        if not re.fullmatch(r"S\d{2}", identifier) or identifier in records:
            errors.append(f"implementation-map: invalid/duplicate ID {identifier!r}")
            continue
        records[identifier] = item
        if item.get("status") not in statuses:
            errors.append(f"{identifier}: unknown implementation status")
        path = inside_research(root, item.get("path", ""))
        if path is None:
            errors.append(f"{identifier}: plan must stay under .research")
        elif not path.is_file():
            errors.append(f"{identifier}: missing plan {item.get('path')}")
        task_ids = set()
        for task in item.get("tasks", []):
            task_id = task.get("id", "")
            if not re.fullmatch(re.escape(identifier) + r"\.\d+", task_id) or task_id in task_ids:
                errors.append(f"{identifier}: invalid/duplicate task {task_id}")
            task_ids.add(task_id)
            if task.get("status") not in statuses:
                errors.append(f"{task_id}: unknown implementation status")
            if task.get("status") == "completed":
                note = inside_research(root, task.get("note", ""))
                if note is None:
                    errors.append(f"{task_id}: task note must stay under .research")
                elif not task.get("note") or not note.is_file():
                    errors.append(f"{task_id}: completed task needs its note")
        if item.get("status") == "completed" and (
                not item.get("tasks") or any(task.get("status") != "completed" for task in item["tasks"])):
            errors.append(f"{identifier}: completed step has unfinished tasks")
    if not records:
        errors.append("implementation-map: no steps defined")
    graph = {}
    for identifier, item in records.items():
        graph[identifier] = []
        for dependency in item.get("depends_on", []):
            if dependency not in records:
                errors.append(f"{identifier}: unknown depends_on reference {dependency}")
            else:
                graph[identifier].append(dependency)
    visiting, visited = set(), set()

    def visit(identifier):
        if identifier in visiting:
            errors.append(f"implementation-map: dependency cycle at {identifier}")
            return
        if identifier in visited:
            return
        visiting.add(identifier)
        for dependency in graph[identifier]:
            visit(dependency)
        visiting.remove(identifier)
        visited.add(identifier)

    for identifier in graph:
        visit(identifier)
    for key in ("plan", "architecture"):
        value = data.get("references", {}).get(key)
        path = inside_research(root, value or "")
        if not value or path is None or not path.is_file():
            errors.append(f"implementation-map: missing reference {key}")
    return errors


def elements(tree, name):
    return [element for element in tree.iter() if element.tag.rsplit("}", 1)[-1] == name]


def property_true(tree, name):
    return any((element.text or "").strip().lower() == "true" for element in elements(tree, name))


def check_project(path):
    errors = []
    tree = ET.parse(path)
    if not path.with_name("packages.lock.json").is_file():
        errors.append(f"{path}: missing packages.lock.json; restore deliberately and commit it")
    for reference in elements(tree, "PackageReference"):
        if any(key in reference.attrib for key in ("Version", "VersionOverride")) or any(
            child.tag.rsplit("}", 1)[-1] in {"Version", "VersionOverride"} for child in reference
        ):
            errors.append(f"{path}: package versions belong in Directory.Packages.props")
    browser = property_true(tree, "IsBrowserTestProject")
    test = property_true(tree, "IsTestProject")
    if browser and not test:
        errors.append(f"{path}: browser test projects must also set IsTestProject=true")
    playwright = any(reference.get("Include", "").startswith("Microsoft.Playwright")
                     for reference in elements(tree, "PackageReference"))
    if test and playwright and not browser:
        errors.append(f"{path}: Playwright tests must declare IsBrowserTestProject=true")
    return errors


def check_workflow(path, content):
    errors = []
    if re.search(r"^\s*pull_request_target\s*:", content, re.MULTILINE):
        errors.append(f"{path}: do not run untrusted PR code through pull_request_target")
    for action, revision in re.findall(r"uses:\s*([^\s@]+)@([^\s#]+)", content):
        if not action.startswith("./") and not re.fullmatch(r"[0-9a-f]{40}", revision):
            errors.append(f"{path}: pin {action} to a full commit SHA")
    return errors


def check_repository(root):
    errors = [f"missing required file: {path}" for path in REQUIRED_PATHS if not (root / path).is_file()]
    paths = repository_files(root)
    for path in paths:
        errors.extend(check_credential_path(path))
        if (path.suffix not in TEXT_SUFFIXES
                and path.name not in {".editorconfig", ".gitattributes", "Dockerfile", ".dockerignore"}
                and path.parent != Path(".githooks")):
            continue
        try:
            content = (root / path).read_bytes().decode("utf-8")
            errors.extend(check_text(path, content))
            if path.suffix == ".md":
                for target in markdown_targets(content):
                    if not (root / path.parent / target).exists():
                        errors.append(f"{path}: missing local link {target}")
            elif path.suffix == ".json":
                data = read_json(root / path)
                if path.as_posix() == ".research/implementation-map.json":
                    errors.extend(check_map(data, root))
                if path.parent == Path(".research") and isinstance(data, dict) and "sources" in data:
                    ids = [item["id"] for item in data["sources"]]
                    if len(ids) != len(set(ids)):
                        errors.append(f"{path}: duplicate source IDs")
                    for source in data["sources"]:
                        url = urlsplit(source.get("url", ""))
                        if url.scheme not in {"http", "https"} or not url.netloc:
                            errors.append(f"{path}: invalid source URL for {source['id']}")
            elif path.suffix in {".props", ".targets", ".csproj", ".slnx", ".config"}:
                tree = ET.fromstring(content)
                if path.suffix == ".csproj":
                    errors.extend(check_project(root / path))
                if path.name == "Directory.Packages.props":
                    for package in elements(tree, "PackageVersion"):
                        if not re.fullmatch(r"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?", package.get("Version", "")):
                            errors.append(f"{path}: use an explicit package version for {package.get('Include')}")
            if path.parent.as_posix() == ".github/workflows":
                errors.extend(check_workflow(path, content))
        except (ValueError, KeyError, TypeError, UnicodeError, ET.ParseError) as error:
            errors.append(f"{path}: invalid content: {error}")
    return errors


def main():
    try:
        errors = check_repository(ROOT)
    except (OSError, subprocess.CalledProcessError) as error:
        print(f"Repository validation failed: {error}", file=sys.stderr)
        return 1
    if errors:
        for error in errors:
            print("ERROR: " + error, file=sys.stderr)
        return 1
    print("PASS: repository paths, text, JSON, plan graph, source catalogs and dependency/workflow contracts")
    return 0


if __name__ == "__main__":
    sys.exit(main())
