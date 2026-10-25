# S01.1 Stack, guardrails, agent setup and Material 3 base

Date: 2026-10-25. Branch: codex/foundation.

## What was built

- Copied from the original repository and adapted: build settings, pinned SDK, locked NuGet restore, editorconfig, Dockerfile/Compose (web, migrate, postgres, verify; workers removed), CI workflow, branch hooks and policy, PR/issue templates, engineering docs.
- `check_repository.py` now validates the MVP map (Sxx steps, Sxx.n tasks with notes) instead of 21 F / 18 D plans. `verify.py` rejects frontend packages and TypeScript by decision. Database recovery tooling was dropped until administration is planned.
- New AGENTS.md, CONTRIBUTING.md, architecture and step plans S01–S06.
- Minimal app: `healthcheck`, `database migrate --confirm-database <name>`, the portal with an empty initial migration, `/health/live` and `/health/ready`.
- Material 3 design system: tokens (light/dark color roles, type scale, shape, elevation, motion, spacing), components, app shell with navigation rail/bar, `<md-icon>` tag helper with a Material Icons sprite, 404 page, and the `/Design` reference page in Development.

## Verification

- `LC_ALL=en_US.UTF-8 python3 scripts/verify.py`: PASS. 56 Python guardrail tests, Release build with warnings as errors, format checks, 13 backend tests (readiness on real PostgreSQL, password file handling, container probe), 9 Chromium tests (no-JavaScript home and search, escaping, rail on desktop, bar and no horizontal scroll at 360px, skip link, light/dark scheme, no console errors including CSP, 404, design tabs).
- Manual: migrate command against a scratch PostgreSQL 18 cluster (succeeds; refuses a mismatched database name), and the portal checked in the browser at desktop light and phone dark.

## Limits

- Not run here: Docker Compose stack and the Docker verify profile, GitHub Actions.
- Fonts are not self-hosted yet; the stack falls back to the platform UI font. Colors are the M3 baseline scheme.
- Local test runs need `LC_ALL` set to a valid locale for the PostgreSQL fixture on macOS.
