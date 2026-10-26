# Public procurement (SEAP) import

Loads the SEAP exports published by the Romanian Digitalization Authority (ADR) on data.gov.ro ("Achiziții publice <year>", CC BY 4.0) into `procurement.awards`: contracts awarded after a procedure and direct purchases, with authority, CPV, dates, value in lei and winner.

## Files and command

All published files from 2007 to 2026 (contracts and direct purchases; data.gov.ro has no 2019 dataset) are pinned in the [source catalog](../../src/BusinessIntelligence.Modules/Registry/Sources/catalog.json) (kinds `seap-contracts`, `seap-direct`) with size, SHA-256, year, period family and `localPath` under `seap-2025/` and `seap-history/<year>/`. The period family is a quarter (`Q1`–`Q4`), a half year (`S1`, `S2`) or the whole year (`Y`); a period split into several files adds `-P<n>`.

```sh
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll procurement import-all .research/raw
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll procurement status
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll procurement summarize
```

In Docker it is a step of `scripts/docker_import_all.py`. Each file imports in one transaction; the same file again is a no-op and a newer file for the same kind and period replaces the older one. After the files, when one of them was published or the totals were never built, `import-all` rebuilds `procurement.year_totals` (yearly counts and values per company as winner and as authority), which the profile reads; `procurement summarize` does only that step, for example after the `ProcurementTotals` migration on a database imported earlier.

## Rules

- Formats: CSV (comma, semicolon or `|`-quoted; UTF-8 and Windows-1250 mixed line by line in some years), .xlsx, .ods and old binary .xls. Every sheet of a workbook is read (older exports split a quarter into sheets of 65,000 rows).
- Columns are found by header name through an alias table, because the names change across years (`DENUMIRE_AC`/`AUTORITATE_CONTRACTANTA`, `CUI_OF`/`CASTIGATOR_CUI`, …); the second "Data publicare" column is the award notice date. A file whose header does not give the required columns is refused. Contract files that repeat the header as machine labels on row 2 skip it.
- Dates are Excel serials, ISO, `dd-MM-yyyy`, `dd.MM.yyyy` or slash dates (month first unless a part is over 12); values are lei as published (invariant or Romanian number format), rounded to 2 decimals; a blank value stays missing. Unpaired surrogates and NUL characters are removed from cells.
- The 2016 H1 contracts CSV has unquoted commas inside names: rows with too many cells are realigned on the date and value columns, and rows that cannot be realigned are skipped and counted (`realignedRows`, `skippedRows` in the import report).
- `awarded_on` is the contract date, else the publication date; the profile's most recent wins are read by `(winner_cui, awarded_on)`.
- A contract with several winners has one row per winner, all with the contract's full value (`winners` counts them, `first_winner` marks one row per contract). The source does not say how the value is shared, so nothing is divided.
- Winner identifiers are read as SEAP writes them (RO prefix, numeric cells). One that is a personal numeric code is not stored, and neither is the winner's name.
- The same contract can appear in several quarterly files. The profile counts a contract once (authority, award notice, lot, contract number), in the first year it appears, with the largest value published for it.
- Values are not corrected: a few contracts carry obviously wrong values at the source (for example 125.9 billion lei for a simulator). The page says so.

## Result

All 131 files: 37,245,934 rows (4.79 million contract rows, 32.46 million direct purchases), about 11 GB in `procurement.awards`, imported in 511 s locally with 2.47 GB peak memory.

### 2025 files

| File | Rows | Personal codes withheld | Blank values |
| --- | ---: | ---: | ---: |
| Contracts Q1–Q4 | 124,086 / 210,099 / 143,317 / 152,321 | 55 / 77 / 20 / 67 | 2,692 / 7,303 / 3,344 / 2,821 |
| Direct purchases Q1–Q4 | 500,390 / 616,106 / 514,263 / 603,526 | 3 / 2 / 5 / 7 | 0 |

34 s for all eight files in Docker (2–7 s each); about 1.3 GB peak memory, because a workbook's distinct strings (about a million in a direct-purchase file) are held while its sheet is streamed. The Docker import service has 3 GB.

## Limits

- 2019 is not published on data.gov.ro.
- The 2024 Q1 contracts CSV is partial (about 21,000 rows); the .xls of the same quarter (151,000 rows) is pinned instead.
- The 2022 Q2 direct-purchase file has 87,929 contract-layout rows appended; they are skipped and counted, and the 2022 Q2 contracts file itself (64,862 rows) may be incomplete.
- From 2021, contract files leave the value blank for many framework agreements; it stays missing.
- Contract modifications and award notices for direct purchases are not imported.
