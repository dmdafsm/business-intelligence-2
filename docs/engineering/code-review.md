# Code review

CI checks deterministic properties. Review checks the behavior, assumptions and risks that CI may miss. The author always inspects the final diff. Feature/data changes also need a review pass from a dedicated review mode or another reviewer before being described as independently reviewed; self-review must be labelled as such.

## What is configured now

AGENTS.md contains review rules and the PR template asks for evidence. The Verify workflow builds and tests. No review service or automatic AI review is activated by these files.

## Local review

In Claude Code, run `/code-review` on the uncommitted changes or the branch diff, or ask a separate review agent. Tell the reviewer the task ID and the risk to inspect. For substantive changes, check correctness, provenance and period semantics, missing versus zero, publication and rerun behavior, migrations, and design-system and accessibility rules. Leave formatting to CI.

The reviewer reports actionable findings with severity, file/line, trigger and consequence; if none are found, it says so and states its limits. No findings is not a guarantee of correctness. Review the final changes after fixes.

## PR review

Keep the PR as the canonical discussion once it exists: review comments, dispositions and validation links belong there.

An AI review is additional evidence. It does not automatically satisfy a GitHub required approval or authorize merge. Require formal independent approval when an eligible reviewer is available and the ruleset is configured. With a sole maintainer, use explicit owner merge judgment and review evidence rather than an impossible self-approval requirement.

Resolve material findings or record a concrete, justified disposition. Re-run affected checks after fixes. Do not dismiss an actual correctness/access/data-loss issue solely to obtain a green status. The review should identify real problems, not invent findings to fill a quota.

## Records

- Local review: `.artifacts/reviews/<task-id>/review.md`, using [the review template](templates/code-review.md). It is generated local output and is ignored by Git.
- PR review: comments/review on the PR, with the reviewed commit/base and dispositions. Link the relevant CI run and artifacts.
- Durable design/product conclusions: a short summary in docs or an ADR under docs/engineering, as appropriate; avoid committing raw transient review output.

Record reviewer or review mode, scope, base/head where available and dirty/untracked state for local work. Link evidence to the exact version inspected. A report that says review has not run must not be presented as completed review.
