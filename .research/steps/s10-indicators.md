# S10 Financial indicators and benchmark

Status: completed. Depends on S04 and S06. Added on 2026-10-26 at the maintainer's request, carried over from the original repository (F04/D14).

## Outcome

A company profile shows a few derived financial indicators for every filed year, each with its formula, and compares the company with companies in the same CAEN class and size band in the same year: median, middle half (P25–P75), the company's position and how many companies the comparison is based on.

## Indicators (`indicators-v1`)

| Key | Name | Formula | Not calculable when |
| --- | --- | --- | --- |
| `turnover_growth` | Creșterea cifrei de afaceri | (CA an − CA an anterior) / CA an anterior | no statement or blank turnover in either year; previous turnover zero |
| `net_margin` | Marja netă | rezultat net / CA | blank result or turnover; turnover zero |
| `turnover_per_employee` | Cifra de afaceri pe salariat | CA / număr mediu de salariați | blank turnover or employees; no employees |
| `return_on_equity` | Rentabilitatea capitalurilor proprii | rezultat net / capitaluri proprii | blank result or equity; equity zero or negative |
| `debt_ratio` | Gradul de îndatorare | datorii / (active imobilizate + active circulante) | a blank component; assets zero |

The net result is the published profit, or minus the published loss. Values stay as published: a blank component is never read as zero, and every value that cannot be calculated says why.

## Benchmark

- Cohort: statements of the same year with the same CAEN class and version, split by size band of average employees (0, 1–9, 10–49, 50–249, 250+) and also for all sizes together.
- Percentiles P10, P25, P50, P75, P90 (continuous) over the companies where the indicator is calculable. Fewer than 10 such companies: no percentiles, only the count.
- 2025 states no CAEN version: the source mixes Rev.2 and Rev.3 codes, and two thirds of the codes exist in both. Since S10.2 a 2025 statement is compared in a derived CAEN Rev.3 class, set only when one class follows. The class comes from the Rev.3 nomenclature, the INS correspondence table (September 2026) and the company's authorized activities in the registry, and each derived class records its method.
  - Statements without a single class (29% of 2025) are left out of the comparison.
  - The profile says how the class was set, or falls back to 2024.
- `financials benchmarks` rebuilds the table in one transaction, and `financials import-all` runs it after importing. Readers keep the previous table until the rebuild commits.

## Acceptance

- Formulas are calculated the same way on the profile and in the benchmark, and the same data give the same result.
- Missing, zero and not applicable stay distinct, with the reason shown.
- Every comparison shows the year, CAEN class and version, size band and the number of companies.
- The section works on a phone and without JavaScript.

## Tasks

- S10.1 Indicators, benchmark table and profile section — [note](../tasks/s10-1-indicators.md).
- S10.2 CAEN Rev.3 classes for 2025 from the INS correspondence table — [note](../tasks/s10-2-caen-correspondence.md).
