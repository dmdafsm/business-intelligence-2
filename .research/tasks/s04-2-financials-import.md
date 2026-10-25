# S04.2 Import MF financial statements 2008–2025

Date: 2026-10-25. Branch: codex/registry. Formats: [S04.1](s04-1-financials-inspection.md). Operation: [financials import guide](../../docs/engineering/financials-import.md).

## What was built

- 94 MF files pinned in the source catalog (year, family, local path, size, SHA-256).
- `financials.statements` keyed by CUI and year, with the family, CAEN code and version, source file, 20 amounts, employees and quality flags.
- Label-based mapping to shared indicators with two corrected dictionary defects.
- `financials import-all|import|status` commands.

## Real-data run

| Years | Statements |
| --- | --- |
| 2008–2010 | 615k–664k a year (BL only) |
| 2011–2015 | 655k–682k a year |
| 2016–2025 | 700k–969k a year |

- Total 13,510,119 statements, 1,867,193 distinct CUIs, 3,725 CUIs missing from the 19.10.2026 ONRC snapshot (to look at when building the profile).
- First run stopped on two 2011 employee counts of 999999999999999 (integer overflow), which led to the range rules. The second run showed a 2020 turnover of 300 trillion lei, which led to the amount rule. Final run: 147 s, about 300 MB peak.
- Totals look plausible after the rules: turnover 924.5 billion lei (2010) to 2,751.9 billion (2024); about 4 million employees a year once 166 unlikely counts are set aside.
- Spot check: CUI 381002 has statements 2008–2017 (turnover rising to 1.1M lei in 2012, nothing after the company stopped reporting).

## Tests

Dictionary mapping (shifted positions, the two defects, Windows-1250, unknown label, gap, repeated indicator); PostgreSQL: blank versus zero, negative values, CAEN padding, duplicate rows, out-of-range employees and amounts, unlikely employees, a revision replacing only its year and family, replay, a header that disagrees with its dictionary changing nothing.

## Limits

- Only the latest publication of each year is kept; earlier publications of the same year are not stored as revisions.
- 2025 CAEN version unknown per row.
- Main activity for grouping comes from the latest statement's CAEN; mapping it to Rev.3 is part of S05.
