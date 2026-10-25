# Task readiness: business analysis and architecture

Use the MVP step plans as the starting point. The implementing agent performs a short business pass and an architecture impact check before editing. These are responsibilities, not mandatory separate people, agents, meetings or approval gates.

## Business pass

For a feature or data task, establish:

1. Who uses the result, what action they take and the observable outcome.
2. The relevant step plan, its acceptance criteria and dependencies; reduce the task to a coherent, implementable behavior.
3. Concrete examples of input and expected output, including important failure cases. Consider legal form, zero versus missing, reference period, coverage and source date where relevant.
4. The source license, attribution and personal-data constraints affected by the change.
5. What evidence will establish the result: unit/fixture, PostgreSQL, browser, migration or performance checks.

Reuse a complete existing plan rather than rewriting it. A small UI bug or REPO task may need only a few sentences in the task/PR. Material new analysis or unresolved product questions belong under `.research/tasks/`, using the [task brief template](templates/task-brief.md).

Do not invent a business rule to avoid an essential clarification. Ask only when the missing decision materially changes behavior or correctness; continue independent safe work. Routine implementation details are for the agent to resolve.

## Architecture impact check

Record one of these outcomes:

- Existing pattern: identify the owning module/slice and applicable design; no new ADR is required.
- Material design change: write the smallest useful design/ADR before the implementation depends on it.
- Essential decision missing: name the missing input and continue work that does not depend on it.

A focused architecture pass is needed for changes to module boundaries or cross-module writes, the shared company schema or publication, the design system, native dependencies, deployment topology, or measured resource constraints.

A local slice following an existing pattern, a formula fix within a documented contract, a copy edit or routine dependency patch does not require a separate architect phase just because code changes. Still check the actual impact; labels alone do not determine risk.

Use [ADR guidance](decisions/template.md) when a decision affects future work. Explain ownership, alternatives, migration/rollback and verification as relevant. Do not turn ordinary reversible choices into additional permission requests or claim acceptance that did not happen.

## Before implementation and before handoff

Begin implementation when the outcome, relevant rules and verification path are clear. The plan can be lightweight and evolve with evidence. Record substantive discoveries back into the relevant plan and research.

Before handoff: run applicable verification/browser checks, inspect the diff, and follow [code review](code-review.md). Self-review, independent review and merge authorization are distinct. A completed readiness note is not implementation evidence or a passing test.
