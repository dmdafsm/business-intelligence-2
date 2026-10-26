"""Regression tests for failure modes that could otherwise produce false confidence."""

from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import check_repository as checks
import verify


class RepositoryContractTests(unittest.TestCase):
    def test_duplicate_json_keys_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "duplicate.json"
            path.write_text('{"id": 1, "id": 2}\n')
            with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
                checks.read_json(path)

    def test_markdown_examples_and_external_links_are_not_local_links(self):
        content = '[guide](guide.md) [web](https://example.com) [anchor](#part)\n```text\n[x](missing.md)\n```\n'
        self.assertEqual(["guide.md"], list(checks.markdown_targets(content)))

    def test_private_key_marker_is_rejected(self):
        content = "-----BEGIN " + "PRIVATE KEY-----\n"
        self.assertTrue(any("private-key marker" in error for error in checks.check_text(Path("key.txt"), content)))

    def test_generated_nuget_lockfile_does_not_require_manual_whitespace_changes(self):
        self.assertEqual([], checks.check_text(Path("packages.lock.json"), '{"version": 1}'))
        self.assertTrue(checks.check_text(Path("manual.json"), '{"version": 1}'))

    def test_real_env_is_rejected_but_examples_are_allowed(self):
        self.assertTrue(checks.check_credential_path(Path(".env.production")))
        self.assertEqual([], checks.check_credential_path(Path(".env.example")))

    def test_unpinned_actions_and_privileged_pr_trigger_fail(self):
        content = "on:\n  pull_request_target:\nsteps:\n  - uses: actions/checkout@v6\n"
        self.assertEqual(2, len(checks.check_workflow(Path("workflow.yml"), content)))

    def test_missing_lock_and_inline_package_versions_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / "Example.csproj"
            project.write_text('<Project><ItemGroup><PackageReference Include="Example" Version="1.0.0" /></ItemGroup></Project>')
            errors = checks.check_project(project)
            self.assertEqual(2, len(errors))

    def test_playwright_tests_must_declare_browser_setup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "packages.lock.json").write_text('{}')
            project = root / "BrowserTests.csproj"
            project.write_text('<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>'
                               '<ItemGroup><PackageReference Include="Microsoft.Playwright.Xunit" /></ItemGroup></Project>')
            self.assertTrue(any("declare IsBrowserTestProject" in error for error in checks.check_project(project)))

    def test_browser_marker_requires_test_project(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "packages.lock.json").write_text('{}')
            project = root / "BrowserTests.csproj"
            project.write_text('<Project><PropertyGroup><IsBrowserTestProject>true</IsBrowserTestProject></PropertyGroup></Project>')
            self.assertTrue(any("also set IsTestProject" in error for error in checks.check_project(project)))


class ApplicationVerificationTests(unittest.TestCase):
    def test_empty_application_is_explicitly_absent(self):
        self.assertIsNone(verify.backend_inventory(Path("."), []))

    def test_csharp_without_project_cannot_be_skipped(self):
        with self.assertRaisesRegex(verify.VerificationError, "without a project"):
            verify.backend_inventory(Path("."), [Path("src/Loose.cs")])

    def test_backend_without_test_project_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / "App.csproj"
            project.write_text("<Project />")
            with self.assertRaisesRegex(verify.VerificationError, "behavioral tests"):
                verify.backend_inventory(root, [Path("App.csproj"), Path("App.slnx")])

    def test_project_requires_one_root_solution(self):
        with self.assertRaisesRegex(verify.VerificationError, "exactly one root"):
            verify.backend_inventory(Path("."), [Path("src/App/App.csproj")])

    def test_frontend_package_is_rejected_by_decision(self):
        with self.assertRaisesRegex(verify.VerificationError, "excluded by decision"):
            verify.verify_frontend(Path("."), [Path("src/ClientAssets/package.json")])

    def test_typescript_is_rejected_by_decision(self):
        with self.assertRaisesRegex(verify.VerificationError, "excluded by decision"):
            verify.verify_frontend(Path("."), [Path("src/ClientAssets/app.ts")])

    def test_missing_test_report_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(verify.VerificationError, "No TRX"):
                verify.require_test_results(Path(directory))

    def test_zero_tests_fails(self):
        self.check_report(executed=0, expected_error="Zero tests")

    def test_failed_tests_fail_even_if_command_returned_zero(self):
        self.check_report(executed=1, failed=1, expected_error="Failed, aborted or skipped")

    def test_skipped_tests_are_not_success(self):
        self.check_report(executed=1, skipped=1, expected_error="Failed, aborted or skipped")

    def test_successful_namespaced_trx_is_accepted(self):
        self.check_report(executed=2)

    def check_report(self, executed, failed=0, skipped=0, expected_error=None):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "results.trx"
            path.write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                '<ResultSummary outcome="Completed">'
                f'<Counters executed="{executed}" failed="{failed}" notExecuted="{skipped}" />'
                '</ResultSummary></TestRun>'
            )
            if expected_error:
                with self.assertRaisesRegex(verify.VerificationError, expected_error):
                    verify.require_test_results(Path(directory))
            else:
                verify.require_test_results(Path(directory))

    def test_stale_report_is_deleted_before_test_execution(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "global.json").write_text('{"sdk":{"version":"10.0.402"}}')
            project = Path("tests/BehaviorTests/BehaviorTests.csproj")
            (root / project.parent).mkdir(parents=True)
            (root / project).write_text('<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>')
            output = root / ".artifacts/test-results" / project.with_suffix("")
            output.mkdir(parents=True)
            stale = output / "old.trx"
            stale.write_text("old result")

            def fake_run(command, *_args, **_kwargs):
                if command[1] == "--version":
                    return "10.0.402\n"
                if command[1] == "sln":
                    return str(project) + "\n"
                return ""

            with patch.object(verify, "backend_inventory", return_value=(Path("App.slnx"), [project], [project])), \
                    patch.object(verify, "require_tool"), patch.object(verify, "run", side_effect=fake_run):
                with self.assertRaisesRegex(verify.VerificationError, "No TRX"):
                    verify.verify_backend(root, [])
            self.assertFalse(stale.exists())

    def test_non_browser_project_does_not_install_browsers(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Unit.csproj").write_text('<Project />')
            with patch.object(verify, "run") as command:
                self.assertIsNone(verify.prepare_browser_test(root, Path("Unit.csproj")))
                command.assert_not_called()

    def test_browser_setup_requires_powershell(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Browser.csproj").write_text('<Project><PropertyGroup><IsBrowserTestProject>true</IsBrowserTestProject></PropertyGroup></Project>')
            with patch.object(verify.shutil, "which", return_value=None):
                with self.assertRaisesRegex(verify.VerificationError, "pwsh"):
                    verify.prepare_browser_test(root, Path("Browser.csproj"))

    def test_missing_built_playwright_script_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Browser.csproj").write_text('<Project><PropertyGroup><IsBrowserTestProject>true</IsBrowserTestProject></PropertyGroup></Project>')
            with patch.object(verify, "require_tool"), patch.object(verify, "run", return_value=str(root / "bin")):
                with self.assertRaisesRegex(verify.VerificationError, "installer is missing"):
                    verify.prepare_browser_test(root, Path("Browser.csproj"))

    def test_browser_setup_uses_built_package_and_isolates_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = Path("Browser.csproj")
            (root / project).write_text('<Project><PropertyGroup><IsBrowserTestProject>true</IsBrowserTestProject></PropertyGroup></Project>')
            output = root / "bin/Release/net10.0"
            output.mkdir(parents=True)
            installer = output / "playwright.ps1"
            installer.write_text('fixture script; never executed')
            evidence = root / ".artifacts/browser-tests/Browser"
            evidence.mkdir(parents=True)
            (evidence / "stale.zip").write_text('old trace')
            with patch.object(verify, "require_tool"), patch.object(verify, "run", side_effect=[str(output), ""]) as command, \
                    patch.object(verify.sys, "platform", "linux"), patch.dict(verify.os.environ, {"BI_BROWSER_SYSTEM_DEPS": ""}):
                environment = verify.prepare_browser_test(root, project)
            self.assertEqual(str(evidence.resolve()), environment["BROWSER_ARTIFACTS_DIR"])
            self.assertFalse((evidence / "stale.zip").exists())
            self.assertEqual(["pwsh", "-NoProfile", "-File", str(installer.resolve()), "install", "--with-deps", "chromium"],
                             command.call_args_list[1].args[0])

    def test_preinstalled_browser_dependencies_still_install_package_matched_chromium(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = Path("Browser.csproj")
            (root / project).write_text('<Project><PropertyGroup><IsBrowserTestProject>true</IsBrowserTestProject></PropertyGroup></Project>')
            output = root / "bin"
            output.mkdir()
            (output / "playwright.ps1").write_text('fixture script; never executed')
            with patch.object(verify, "require_tool"), patch.object(verify, "run", side_effect=[str(output), ""]) as command, \
                    patch.object(verify.sys, "platform", "linux"), patch.dict(verify.os.environ, {"BI_BROWSER_SYSTEM_DEPS": "preinstalled"}):
                verify.prepare_browser_test(root, project)
            self.assertEqual(["install", "chromium"], command.call_args_list[1].args[0][-2:])


if __name__ == "__main__":
    unittest.main()
