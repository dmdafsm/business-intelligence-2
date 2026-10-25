# Browser testing and reports

Browser verification is required for changed user-visible flows. Backend tests remain necessary for data rules; a screenshot cannot prove them.

## Setup

The suite in `tests/BusinessIntelligence.BrowserTests` uses Playwright .NET with xUnit/VSTest, so browser cases share the language and TRX pipeline of backend tests. It sets IsTestProject=true and IsBrowserTestProject=true. [Playwright .NET runners](https://playwright.dev/dotnet/docs/test-runners).

The verifier builds .NET, resolves the browser project's Release output, installs its package-matched Chromium through the generated playwright.ps1 (PowerShell/pwsh required), and runs it with dotnet test. Missing tools fail instead of passing. Linux setup includes OS dependencies. [Installation](https://playwright.dev/dotnet/docs/intro), [CI](https://playwright.dev/dotnet/docs/ci).

The fixture starts a real local Kestrel endpoint and its own isolated PostgreSQL cluster with synthetic data, and disposes only those resources. The Docker verification image preinstalls PowerShell and Linux browser dependencies; `BI_BROWSER_SYSTEM_DEPS=preinstalled` skips only the redundant privileged apt step there.

## What the suite covers

- Every page works with JavaScript disabled; scripts only enhance it.
- Desktop and phone widths (no horizontal page scroll at 360px), light and dark color schemes.
- Keyboard access: skip link, visible focus, logical tab order.
- Search, grouping filters and the company profile as they are delivered, including empty and missing-data states.
- Imported text is escaped.

Use an isolated BrowserContext per case, role/label locators and retrying assertions. Avoid fixed sleeps, fragile CSS chains and unbounded retries.

Manual checks complement the suite for layout, focus, charts and visual polish. Use a local app and synthetic or imported data, and record the build, steps and results with [the browser report template](templates/browser-report.md).

## Evidence

The runner supplies an absolute BROWSER_ARTIFACTS_DIR per browser project; fixtures write test-specific subdirectories there. Record traces and screenshots on failures, and close the trace before disposing the context without masking the original failure. Open a trace with `pwsh <Release-output>/playwright.ps1 show-trace <trace.zip>`.

| Evidence | Local location | CI location |
| --- | --- | --- |
| .NET results, including browser cases | `.artifacts/test-results/<project-path>/results.trx` | `backend-test-results` artifact |
| Browser screenshots/traces | `.artifacts/browser-tests/<project-path>/<test>/` | `browser-test-evidence` artifact |
| Manual browser report | `.artifacts/browser-tests/manual/<task>/report.md` | Link from the PR |
| Code review | `.artifacts/reviews/<task>/review.md` | PR review/comments |

CI artifacts are kept seven days. Keep lasting summaries in the task note. Do not commit traces, screenshots or test databases.
