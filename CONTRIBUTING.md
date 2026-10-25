# Contributing

Start with [AGENTS.md](AGENTS.md), the [MVP plan](.research/mvp-plan.md) and the [implementation map](.research/implementation-map.json).

Run `python3 scripts/install_hooks.py` once per clone, then work on a `codex/<topic>` branch. main is protected locally and integrates through PRs. See the [branch policy](docs/engineering/branch-policy.md); local hooks do not replace GitHub rulesets.

## Workflow

1. Pick the next task of an MVP step (Sxx.n) or a `REPO` maintenance task. Read the step plan and its dependencies.
2. Inspect the existing code and do the [readiness pass](docs/engineering/task-readiness.md): the smallest coherent result and the evidence needed to accept it.
3. Implement following the [coding standards](docs/engineering/coding-standards.md), the [security/data rules](docs/engineering/security-and-data.md) and the [design system](docs/engineering/design-system.md). Record new research in `.research/`.
4. Run focused checks while working, then `python3 scripts/verify.py`. See the [test matrix](docs/engineering/testing-and-verification.md).
5. Review the diff, write the task note in `.research/tasks/` and update implementation-map.json. Follow [code review](docs/engineering/code-review.md) and [browser verification](docs/engineering/browser-testing.md). Open a PR only when asked, using the template.

Use English identifiers, code comments and engineering docs. UI text is Romanian, with diacritics.

## Toolchain

- Python 3.9+ for the dependency-free repository verifier; CI uses Python 3.12.
- The exact .NET SDK in [global.json](global.json). Roll-forward and prereleases are disabled.
- PostgreSQL server binaries on Linux/macOS, through `BI_TEST_POSTGRES_BIN` or `pg_config`. Tests create private disposable clusters and never use an existing application database.
- PowerShell (`pwsh`) to install the Playwright Chromium build for browser tests. The Docker verify profile includes it.

Alternatively, run the same verifier in the [Docker verification profile](docs/engineering/docker-local.md), which needs no host .NET or PostgreSQL.

For research/documentation-only edits, `python3 scripts/verify.py --repository-only` runs the repository checks and their tests. CI always runs the full verifier.

## Dependencies and decisions

Keep NuGet versions in Directory.Packages.props and commit packages.lock.json files. Regenerate affected locks deliberately with `dotnet restore <solution> --force-evaluate`; the verifier restores in locked mode.

Before adding a dependency, check its official documentation, compatibility, license and maintenance. Record the reason in `.research/`. Use [the decision template](docs/engineering/decisions/template.md) for a material architecture, deployment or security tradeoff.

## Definition of done

- The named behavior works, with evidence appropriate to its risk.
- Provenance, idempotency and bounded resource use are preserved where affected.
- Formatting, analyzers, build and tests pass. Skipped or unavailable checks are listed as such.
- Schema changes include reviewed migrations and an upgrade/recovery procedure.
- Docs, plans and lockfiles match the implementation. No secrets, bulk source files, unrelated changes or fabricated completion claims.

See [maintainer setup](docs/engineering/maintainer-setup.md) for GitHub settings that repository files cannot activate.
