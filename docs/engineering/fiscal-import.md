# Fiscal identification import

Loads the Ministry of Finance taxpayer identification releases (data.gov.ro, "date de identificare plătitori", 2018–2026) into `fiscal.taxpayer_periods`: the fiscal address, phone, fax, VAT, fiscal status and registered tax obligations of each taxpayer, as change-only periods.

## Files and command

The 135 files of the nine releases are pinned in [catalog.json](../../src/BusinessIntelligence.Modules/Registry/Sources/catalog.json) (kind `mf-identification`) with size, SHA-256, release date and `localPath`. Formats, coverage and source defects per release are in the [inspection note](../../.research/tasks/s03-1-fiscal-inspection.md).

```sh
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll fiscal import-all .research/raw [--rebuild]
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll fiscal status
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll fiscal show <cui>
```

Releases apply oldest first, one transaction each. An applied release is skipped; an older one is refused, so adding a missing year means `--rebuild` (truncate and replay everything). Before the pre-2022 releases, the command builds `fiscal.taxpayer_kinds` from the 2022+ files. The table is vacuumed after each release.

## Rules

- A period is a stretch of releases in which the taxpayer had the same name, type, unit type, county, locality, sector, street and number, phone, fax, registry reference, VAT status, fiscal status and its date, deregistration date and obligation set. Names and places are compared folded (case, diacritics, punctuation and legal-form spellings ignored); a type, unit type or registry reference that a release leaves blank is not a change.
- Coverage differs by release (struck-off taxpayers until 2020, only 1.03M rows in 2021), so a taxpayer missing from a release keeps its period; `releases` counts the releases that listed it.
- The 2024 file lost ă, â, î, ș and ț (U+FFFD). In comparisons a lost letter matches any one letter; a period keeps the intact spelling of an earlier release.
- Individuals keep name, county, locality, sector and postal code only. An individual is `PF` in the release, or in any 2022+ release, or has an `F` registry reference; a pre-2022 taxpayer with no type and no `J`/`C` reference is treated the same way. Their street, phone and fax never leave the staging table.
- Obligations are ANAF codes (100 profit tax, 120 micro-enterprise tax, 602 payroll tax…). Releases before 2022 give the date each was registered (`obligation_dates`) and the VAT registration date; later ones only DA/NU.
- Placeholders become missing: `-`, `null`, phone or fax `0` and numbers under six digits. Five-digit postal codes get their leading zero back.
- A CUI listed twice in one release keeps its most recently processed row.

## Result on the downloaded files

All nine releases in 621 s on an Apple M-series laptop with local PostgreSQL 18 (about 325 MB peak memory; the taxpayer-type pass takes 64 s, each release 24–108 s). 7,970,233 periods for 5,252,927 taxpayers; 3.6M have one period, the most has 9. The table takes 3.3 GB with its index.

| Release | Taxpayers | Extended | Opened |
| --- | ---: | ---: | ---: |
| 09.08.2018 | 3,872,279 | 0 | 3,872,279 |
| 05.07.2019 | 4,038,788 | 3,502,540 | 536,248 |
| 05.05.2020 | 4,249,667 | 3,890,302 | 359,365 |
| 18.06.2021 | 1,033,466 | 884,138 | 149,328 |
| 21.06.2022 | 2,358,007 | 1,767,422 | 590,585 |
| 31.08.2023 | 2,460,561 | 1,649,285 | 811,276 |
| 10.07.2024 | 2,509,354 | 2,012,132 | 497,222 |
| 30.06.2025 | 2,559,908 | 1,962,523 | 597,385 |
| 23.07.2026 | 2,647,765 | 2,091,220 | 556,545 |

- 2023 opens more periods: 540k continuing taxpayers have a different obligation set than in 2022 (cause not checked).
- 3,864,764 taxpayers have a CUI that is also an ONRC company; the others (public institutions, associations, liberal professions, taxpayers struck off before the ONRC snapshot) are kept.
- Taxpayer type: 2,994,349 CUIs typed by the 2022+ releases; in 2018–2020 about 16% stay unknown and are treated as individuals.
- Repairs: 7 split records joined, 615 street separators merged, 6 broken 2021 records skipped, 58 duplicate CUIs merged; 104 unparsable dates are missing.

## Not covered yet

The MF releases are not linked to registry periods (the profile reads both by CUI). In Docker, `scripts/docker_import_all.py` runs this import with the raw files mounted read-only (see [the Docker guide](docker-local.md)).
