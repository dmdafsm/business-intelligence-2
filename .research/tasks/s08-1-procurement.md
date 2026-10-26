# S08.1 Public procurement (SEAP) 2025

Date: 2026-10-25. Branch: codex/representatives. Operation: [procurement import guide](../../docs/engineering/procurement-import.md).

## What was built

- The eight 2025 SEAP exports (contracts and direct purchases, Q1–Q4) copied from the original repository (same SHA-256 as its pins) and pinned in the catalog.
- `StreamingXlsx`: a bounded streaming reader for large .xlsx files (shared strings in memory, rows streamed).
- Procurement module, table `procurement.awards`, migration `Procurement`; `procurement import-all|status`; Docker import step.
- Profile section "Achiziții publice": yearly counts and values as winner and as contracting authority (each contract once across quarters and winners), the 20 most recent wins, coverage and sources.

## Real-data run

2,863,908 rows from eight files in 34 s in Docker, 1.3 GB peak locally. 236 winners withheld because their identifier was a personal code. Found and fixed: the per-contract winner count was first computed with an UPDATE on freshly copied rows (no planner statistics) and ran for over 12 minutes on one file; it now comes from a window function over the staging table. Contracts repeated across quarterly files are counted once on the profile. DEDEMAN SRL's page shows 1 contract and 54,948 direct purchases in 2025 and renders in about 130 ms.

## Tests

Workbook reading (shared and inline strings, missing cells, date serials); identifiers (RO prefix, numeric cells, personal codes in exponent form); contract import (winners per contract, first winner, withheld personal code, values and dates, replay); direct purchases (EU funding, blank value); wrong header refused; profile counts a contract once across quarters. Browser: the section, a row and the source note on the profile.

## Limits

- Only 2025. Earlier years and 2026 need downloads and readers for CSV, ODS and old binary XLS.
- Values are as published; a few are obviously wrong at the source.
- Contract modifications are not applied.
