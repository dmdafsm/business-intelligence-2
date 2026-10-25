## Problem and result

Related tasks: Sxx.n / REPO. Describe the trigger, resulting behavior and relevant acceptance criteria.

Business readiness: link the existing plan or brief with concrete acceptance examples. Architecture impact: existing pattern/module, or material change with linked decision.

## Validation

| Command or check | Actual result |
| --- | --- |
| `python3 scripts/verify.py` | Passed / failed / not run, with reason |
| Relevant integration/browser/manual evidence | Result or not applicable, with reason |

## Data, access and operational impact

Describe affected source semantics and provenance, migrations/backfills and import compatibility. For schema/data changes, include the upgrade and recovery procedure. Use “not applicable” with a reason when appropriate.

## Review notes

Material tradeoffs, dependency/license changes, screenshots for UI changes, and known limitations. Link an ADR/research when needed. Delete unused guidance; do not claim checks that were not run.

Review status and scope: self-review / independent review / not run, reviewer or mode, and reviewed commit/diff. Link the PR review and exact CI run/artifacts. For UI/auth changes, link browser scenarios, TRX and relevant screenshots/traces; state not applicable or blocked accurately.

- [ ] The diff is focused and affected plans/lockfiles match the implementation.
- [ ] Applicable acceptance criteria have evidence; incomplete work is stated explicitly.
- [ ] No secrets, unnecessary personal data or bulk source files are included.
