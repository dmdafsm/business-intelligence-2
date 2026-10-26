# S11.1 Market statistics

Date: 2026-10-26. Branch: codex/s11-market. Plan: [S11](../steps/s11-market.md).

## Research first

Insolvency and published tax debts came before this step in the queue. Neither has an open bulk source:

- ANAF restante is an interactive JSF form.
- data.gov.ro has only a list as of 31.03.2016, with no license.
- The BPI is a paid service.

The insolvency state is already on the profile from OD_FIRME (S02.3).

## What was built

- **Module `Market`, schema `market`, migration `MarketStatistics`:**
  - `registrations`: month of first registration × county × kind × current state.
  - `lifecycle_changes`: registrations whose state differs from the previous yearly snapshot.
  - `builds`.
- **`market build`:** rebuilds the tables in one transaction and is the last command of the ONRC job.
  - Locally it took 12 s and produced 123,506 rows, covering 4,126,414 entities.
- **`/statistici` page:**
  - Summary figures, plus monthly (24 complete months) and yearly charts with tables.
  - Share of each year's registrations still active, state counts, state changes between snapshots, and per-county counts.
  - County and kind filters in the URL.
  - "Statistici" added to the navigation (bar-chart icon).
- **`ChartPoint` label:** gained an optional label, so charts can show months or every fifth year instead of crowded two-digit years.

## Tests

- **PostgreSQL:** totals equal the registry count under the same filters, and a rebuild does not double counts. The snapshot month is partial, a state change is attributed to the snapshot pair, an empty filter gives zeros, and no build gives no report.
- **Browser:**
  - Without JavaScript: navigation, GET filters in the URL, the undated-changes note and the reset link.
  - Phone: no page-level horizontal scroll.
- **Manual browser check** of the real data at 1280 px and 375 px, for all of Romania and for Cluj with societăți.

## Limits

- Struck-off, dissolution and insolvency counts are per snapshot interval (about a year). ONRC publishes no event dates.
- A company is counted in its current county, not the county where it was first registered.
- Recent months may rise as late registrations reach the next OD_FIRME edition.
