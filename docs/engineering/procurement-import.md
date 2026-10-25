# Public procurement (SEAP) import

Loads the quarterly SEAP exports published by the Romanian Digitalization Authority (ADR) on data.gov.ro ("Achiziții publice <year>", CC BY 4.0) into `procurement.awards`: contracts awarded after a procedure and direct purchases, with authority, CPV, dates, value in lei and winner.

## Files and command

The 2025 files (contracts and direct purchases, Q1–Q4) are pinned in the [source catalog](../../src/BusinessIntelligence.Modules/Registry/Sources/catalog.json) (kinds `seap-contracts`, `seap-direct`) with size, SHA-256, year, quarter and `localPath` under `seap-2025/`.

```sh
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll procurement import-all .research/raw
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll procurement status
```

In Docker it is a step of `scripts/docker_import_all.py`. Each file imports in one transaction; the same file again is a no-op and a newer file for the same kind and quarter replaces the older one.

## Rules

- The workbook header is checked; columns are taken by position (the header repeats "Data publicare"). Contract files repeat the header as machine labels on row 2, which is skipped. Dates are Excel serials; values are lei as published, rounded to 2 decimals; a blank value stays missing.
- A contract with several winners has one row per winner, all with the contract's full value (`winners` counts them, `first_winner` marks one row per contract). The source does not say how the value is shared, so nothing is divided.
- Winner identifiers are read as SEAP writes them (RO prefix, numeric cells). One that is a personal numeric code is not stored, and neither is the winner's name.
- The same contract can appear in several quarterly files. The profile counts a contract once (authority, award notice, lot, contract number), in the first year it appears, with the largest value published for it.
- Values are not corrected: a few contracts carry obviously wrong values at the source (for example 125.9 billion lei for a simulator). The page says so.

## Result on the 2025 files

| File | Rows | Personal codes withheld | Blank values |
| --- | ---: | ---: | ---: |
| Contracts Q1–Q4 | 124,086 / 210,099 / 143,317 / 152,321 | 55 / 77 / 20 / 67 | 2,692 / 7,303 / 3,344 / 2,821 |
| Direct purchases Q1–Q4 | 500,390 / 616,106 / 514,263 / 603,526 | 3 / 2 / 5 / 7 | 0 |

34 s for all eight files in Docker (2–7 s each); about 1.3 GB peak memory, because a workbook's distinct strings (about a million in a direct-purchase file) are held while its sheet is streamed. The Docker import service has 2.5 GB.

## Not covered

Earlier years (2007–2024) and 2026 are published too, in CSV, XLSX, ODS and old binary XLS, with schemas that change over time; they are not pinned or imported yet. Contract modifications and award notices for direct purchases are not imported.
