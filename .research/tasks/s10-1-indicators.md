# S10.1 Financial indicators and benchmark

Date: 2026-10-26. Branch: codex/s10-indicators. Plan: [S10](../steps/s10-indicators.md).

## What was built

- `FinancialIndicators` (`indicators-v1`): turnover growth, net margin, turnover per employee, return on equity and debt ratio, each with its formula and a reason when it cannot be calculated (no previous statement, blank component, zero turnover or employees, equity zero or negative).
- Table `financials.benchmarks`, migration `FinancialBenchmarks`: P10–P90 per year, CAEN class and version, size band and all sizes. Percentiles are stored only for 10 or more companies; the counts are always kept.
- `financials benchmarks` rebuilds the table in one transaction. `financials import-all` and `financials import` rebuild it after importing. The full rebuild over 13.5 million statements took 104 s locally and produced 259,770 rows.
- Profile: a "Indicatori calculați" table for every filed year, and a comparison with the CAEN class for the latest year with a known CAEN version (median, P25–P75, position, cohort size). The company's size band is used when it has percentiles, otherwise all sizes. A "Cum se calculează" section lists the formulas.

## Tests

- Formulas: zero versus blank, and a loss read as a negative result.
- PostgreSQL: the SQL percentiles equal the median of the C# formula values for every indicator. A deliberately wrong SQL formula made this test fail.
- PostgreSQL: small cohorts get no percentiles, the 2025 rows with unknown version are left out, and a rebuild is idempotent.
- Profile query: comparison year, band fallback and missing reason.
- Browser (no JavaScript): the derived table, the comparison table and the reasons.
- Manual browser check of DEDEMAN (`/firma/2816464`) at 1280 px and 375 px: there is no page-level horizontal scroll, and the first columns stay sticky.

## Limits

- 2025 is not benchmarked. The source mixes CAEN Rev.2 and Rev.3, and two thirds of its codes exist in both versions, so the profile compares against 2024.
- Cohorts use the CAEN class declared in each statement, not the registry's main activity.
- Geography (county) cohorts, other indicators (liquidity needs short-term debt, which UU forms do not separate) and benchmark pages outside the profile are not built.
