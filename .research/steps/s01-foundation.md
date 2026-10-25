# S01 Foundation

Status: completed. Part of the [MVP plan](../mvp-plan.md).

## Outcome

A runnable, verified skeleton the data steps build on: .NET 10 Razor Pages app with PostgreSQL, operator migration command, Docker stack, verifier, hooks, CI, agent instructions, and the Material 3 design system with the app shell.

## Acceptance

- `python3 scripts/verify.py` passes: repository checks, locked restore, Release build, format, backend tests on real PostgreSQL, browser tests in Chromium.
- The repository check validates this MVP plan (Sxx steps, Sxx.n tasks) instead of the old F/D plans, and rejects frontend packages.
- Material 3 tokens (color roles for light and dark, type scale, shape, elevation, state layers, motion) and components (buttons, search bar, text field, chips, cards, list, data table, tabs, empty state, navigation rail and bar, top app bar) exist without a frontend package.
- Pages work without JavaScript, have no horizontal scroll at 360px, follow the system color scheme and have a working skip link.

## Tasks

- S01.1 Stack, guardrails, agent setup and Material 3 base.
