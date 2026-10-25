# S04.1 MF financial statements inspection

Date: 2026-10-25. Downloaded with the maintainer's approval: 94 files (47 data files and their dictionaries, 1.2 GB) listed in [the download plan](download-plan.md); size and SHA-256 of every file in [mf-financials-downloaded.json](../mf-financials-downloaded.json). Profiling script: [inspect_mf_financials.py](../tools/inspect_mf_financials.py).

## Layout

- Data files: comma-separated, header row (`CUI,CAEN,I1…In`), UTF-8, no quoting, integer values in lei (no decimals in any file). The original repository read the 2024–2025 files as headerless; the downloaded files all have a header.
- Dictionaries: `;`-separated `label;key;`, UTF-8 for BL/UU, Windows-1250 for IR from 2016. They name each `In` column.
- Families: `BL_BS_SL` (long, short and simplified balance sheets; the main family until 2014, large companies afterwards), `UU` (small entities; tiny until 2014, the main family from 2015), `IR` (IFRS reporters, 71–112 companies a year). No company appears in two families in the same year.

| Years | BL rows | UU rows | IR rows |
| --- | --- | --- | --- |
| 2008–2010 | 615k–664k | — | — |
| 2011–2014 | 641k–662k | 13k–14k | 71–107 |
| 2015 | 131k | 551k | 79 |
| 2016–2025 | 72k–107k | 594k–885k | 95–112 |

## Indicators

Nine dictionary layouts exist; all are subsets of 21 indicators in the same order: fixed assets, current assets, inventories, receivables, cash and bank, prepaid expenses, liabilities, deferred income, provisions, equity, subscribed capital (`CAPITAL` in early UU), régie patrimony, public patrimony (BL ≤2015, IR ≤2015), net turnover, total income, total expenses, gross profit, gross loss, net profit, net loss, average number of employees. Columns are mapped to these by label, not by position, because positions shift when a layout drops a field.

Dictionary defects, handled explicitly:
- UU 2014–2015 label column `i16` "PIERDERE NETA" (net loss), but it sits between gross profit and net profit and `i18` is also "PIERDERE NETA": `i16` is the gross loss.
- IR 2018–2025 key the régie patrimony column `12` instead of `i12`.

## Values

- Every CUI has a valid check digit. CAEN codes lose their leading zero (`111` for `0111`, 3–4% of rows); 2008–2024 codes are all CAEN Rev.2; 2025 mixes Rev.2 and Rev.3 codes, so the 2025 version cannot be assigned per row.
- Blank and `0` are both used (for example 2024 UU: 813k blank régie patrimony, 565k zero gross loss). They stay distinct.
- Negative values are real: equity is negative for 34% of 2024 UU filers; cash, receivables and current assets also appear negative in a few thousand rows. Profit and loss are separate non-negative columns.
- Duplicate CUIs inside a file occur 0–18 times a year and are always identical rows.
