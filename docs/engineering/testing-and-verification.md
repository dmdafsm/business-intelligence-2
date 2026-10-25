# Testing and verification

## Entry points and current state

Run `python3 scripts/verify.py` from the repository root. It runs repository validation, the verifier's regression tests, and applicable backend/frontend checks. It stops on a failed required command and reports absent application stacks as `NOT APPLICABLE`.

`python3 scripts/verify.py --repository-only` is for documentation/research/tooling work when no application verification is claimed. `python3 scripts/check_repository.py` runs only static contracts. `python3 -m unittest discover -s scripts/tests -v` exercises the guards, including failure cases.

Do not describe a repository pass as a tested feature, deployed workflow or performance result.

## Backend contract

All projects belong to the root .slnx. Test projects set IsTestProject=true and use xUnit with VSTest/TRX.

The full verifier runs locked restore with audit/warnings as errors, Release build, format verification and each test project with a separate TRX report. It requires at least one executed test per test project and no failed/aborted run. Reports are cleared before execution so stale files cannot satisfy the check. Integration prerequisites are not silently converted to skipped success.

The root solution must contain all discovered projects. Tests and source files cannot sit outside a project unnoticed. Do not commit a placeholder suite whose only assertions check framework startup or reproduce implementation details.

## Select checks by changed behavior

| Change | Evidence needed |
| --- | --- |
| Documentation/research | Valid paths, JSON/catalog/map consistency, accurate verification level and no implementation claims. |
| Formula, parser, indicator mapping | Unit/fixture tests with boundary cases, missing/zero distinction, periods and revisions; explicit expected values derived independently. |
| SQL, COPY, transaction, publication | Integration tests against real disposable PostgreSQL; test rollback, incomplete publication and concurrent/idempotent replay where relevant. |
| HTTP/UI/API | Relevant integration/contract checks and browser verification for changed flows, including no-JavaScript behavior and accessible errors. |
| Import command | Rerun and resume after interruption, rejected rows, bounded memory and atomic publication, using controlled fixtures. |
| Migration/backfill | Empty database creation and upgrade from representative prior state, bounded backfill, compatibility/recovery evidence. |
| Query/import performance | Representative row count, hardware, concurrency and cold/warm cache; include simultaneous import if web latency can be affected. |
| Tooling/CI guard | Positive and negative cases; verify broken inputs fail and missing application prerequisites do not report success. |

Use the minimum useful suite for the change. No universal coverage percentage: protect critical invariants and meaningful regressions. Mocks are useful at external boundaries; do not mock an entire EF/SQL stack to claim database correctness.

## Fixtures and isolation

Normal CI is deterministic and does not contact public data sources or need production credentials. Network access for pinned dependency restore is separate from live connector verification. External services use controlled responses and explicit timeout/error cases.

Use synthetic data by default. Sanitize derived samples, record provenance/format versions and check reuse rights. Keep bulk downloads under ignored .research/raw with committed small manifests. Never place database dumps or personal fields in test artifacts.

Integration databases must be dedicated and disposable. Containers can supply this isolation; if a configured database is used, validate that it is a test target and never fall back to production/application settings. Cleanup must only affect the test's own resources.

Use deterministic time and IDs where observable behavior depends on them. Avoid arbitrary sleeps; wait on bounded conditions. Ensure parallel tests do not share mutable state or reuse schemas unsafely.

## UI verification

There is no frontend package; the verifier rejects package.json and TypeScript under src/tests. The Playwright .NET suite is marked IsBrowserTestProject=true and runs through the same TRX contract. Setup and evidence follow [browser testing](browser-testing.md).

Keep a small end-to-end suite around search, grouping filters and the company profile. For visual changes, check desktop and phone widths, light and dark themes, keyboard use and the page with JavaScript disabled. A screenshot alone does not prove data semantics.

Review evidence follows [code review](code-review.md). Start tasks with the [readiness pass](task-readiness.md).

## Honest results and exceptions

Record commands and outcomes, not just checkmarks. Separate passed, failed, not run and not applicable. A missing SDK, container runtime, database or browser is a failed prerequisite for affected checks, not a pass. Run independent checks while diagnosing it.

Never weaken a rule or skip a failing test to complete a feature. A legitimate narrow suppression or temporary exception needs the reason, impact, owner and removal condition visible in the PR/decision record. Do not add an approval ritual to routine fixes; keep the concrete limitation reviewable.
