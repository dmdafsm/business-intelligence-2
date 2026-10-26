# S12.1 Turnover and employee filters

Date: 2026-10-26. Branch: codex/s12-prospecting (on top of S10 and S11). Plan: [S12](../steps/s12-prospecting.md).

## What was built

- **`search.companies`**, migration `SearchFinancials`: new columns `financial_year`, `turnover`, `employees` and `net_result`, taken from the company's statement for the latest year in `financials.statements` (2025 locally), plus indexes on turnover and employees. The search build records the year in its report. Employee counts flagged as unlikely by the MF import, and counts of 1,000 or more with no turnover, are stored as null. In 2025 this rule matched one statement, a company declaring 122,690 employees and no turnover.
- **`CompanySearch`**:
  - New `FinancialFilter`: turnover and employee bounds (inclusive) and ordering by turnover or by employees. Companies without the value come last.
  - Any financial filter or ordering is counted live, like a locality filter, so totals and facets agree with the list.
- **Search page**:
  - A "Date financiare 2025" form in the filters column: two ranges and an order, submitted by GET.
  - Removable chips for each bound and for the ordering.
  - With a financial filter on, each result shows its turnover and employees for that year.
  - Bounds keep only their digits, so "1.000.000" works.

## Tests

- **PostgreSQL**:
  - Only the latest year counts, and an earlier turnover never stands in for it.
  - A blank turnover does not match even a minimum of 0.
  - Inclusive ranges.
  - Ordering keeps every company.
  - An implausible employee count is excluded. The test fails if that rule is removed.
- **Browser (no JavaScript)**: the form submits and keeps the county, the result line shows the year's values, and thousands separators work. The chip removes the filter.
- **Manual check on the real data**: companies with 250 or more employees, ordered by employees, list CFR, Kaufland, Romsilva and Dedeman first, after the bad row is excluded.

## Limits

- Broad national filters (for example any turnover, ordered) take about 2–3 s locally. They scan the whole search table before counting facets.
- With JavaScript off, the submitted URL also carries the empty fields; links built afterwards drop them.
- Profit, CAEN benchmarks and other indicators are not filters yet.
