# S04 Financial history

Status: planned. Depends on S02. Part of the [MVP plan](../mvp-plan.md).

## Outcome

Every company's yearly financial statements from 2008 to 2025, mapped to one shared set of indicators (revenue, net profit/loss, employees, total assets, equity, debts and the rest of each form) with the original form, year and dictionary kept.

## Sources

MF "Situații financiare" on data.gov.ro, one dataset per year 2008–2025, plus the June 2026 revision of 2024. Each year has several form families (for example `WEB_UU_AN`, `WEB_BL_BS_SL_AN`, `WEB_IR`, and banks, insurers, NGOs), each with its own dictionary.

## Acceptance

- An inspection records every year's files, dictionaries, sizes and hashes, and the mapping of each dictionary column to the shared indicators.
- Imports keep blank and zero distinct, keep profit and loss columns as published, and never compute a value the source does not state without labeling it as derived.
- Revisions (such as 2024 updated in 2026) are kept alongside the original, with the latest shown by default.
- Form families that do not apply to companies are recorded and skipped deliberately.

## Tasks

To be split during readiness: inspection of all years, indicator mapping, import per form family, revisions.
