# Financial statements import

Loads the Ministry of Finance annual statements (data.gov.ro, 2008–2025) into `financials.statements`, one row per company (CUI) and year.

## Files and command

The 94 files (data and dictionary for each year and family) are pinned in [catalog.json](../../src/BusinessIntelligence.Modules/Registry/Sources/catalog.json) with their size, SHA-256 and `localPath` under the raw-data folder. Each entry also has its download URL.

```sh
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll financials import-all .research/raw
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll financials import 2024 UU .research/raw
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll financials status
```

Each year and family imports in its own transaction. An already imported file is skipped; a newer file for the same year and family (a revision) replaces that year and family only.

## Rules

- Columns map to 21 shared indicators by their dictionary label ([MfIndicatorMap.cs](../../src/BusinessIntelligence.Modules/Financials/MfIndicatorMap.cs)). Two dictionary defects are corrected and reported: UU 2014–2015 label the gross loss "PIERDERE NETA", and IR 2018+ key a column `12`.
- Amounts are lei as published (`numeric(20,0)`). Blank stays null and differs from 0. Profit and loss stay in separate columns; nothing is derived at import.
- CAEN codes get their leading zero back. They are Rev.2 up to 2024; 2025 mixes Rev.2 and Rev.3, so its version is left empty.
- Impossible values are stored as missing and flagged in `quality_flags`: amounts of 1 trillion lei or more (`amount_out_of_range`), employee counts outside 0–1,000,000 (`employees_out_of_range`). `employees_unlikely` keeps the value but marks 1,000+ employees with turnover under 1,000 lei each.
- Identical duplicate rows in one file are merged; differing duplicates would keep the most complete row.

## Result on the downloaded files

13,510,119 statements for 1,867,193 companies; 3,725 of those CUIs are not in the current ONRC snapshot. About 150 s for all 47 files on a laptop, about 300 MB peak memory. Flags: 5 amounts out of range, 26 employee counts out of range, 166 unlikely employee counts.
