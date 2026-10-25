# S03.1 MF taxpayer identification files: inspection

Date: 2026-10-25. Downloaded with the maintainer's approval: every yearly release 2018–2025 from data.gov.ro (138 files, 5.8 GB; the 2018 and 2019 sizes were not published, so the estimate before downloading was 4.7 GB), listed in [mf-identification-download-manifest.json](../mf-identification-download-manifest.json), with size and SHA-256 per file in [mf-identification-downloaded.json](../mf-identification-downloaded.json). The July 2026 update (parts A and B) is copied from the original repository; its hashes match that repository's manifest. Profile: [mf-identification-inspection.json](../mf-identification-inspection.json), made with [inspect_mf_identification.py](../tools/inspect_mf_identification.py). Publisher: Ministerul Finanțelor, CC BY 4.0.

## Releases

| Release | Files | Rows | Layout |
| --- | --- | --- | --- |
| August 2018 | 42 counties (Mureș replaced, see below) | 3,872,310 | quoted CSV, no header, 39 columns |
| July 2019 | 42 counties (no Maramureș on data.gov.ro) | 4,038,808 | as 2018 with 40 columns |
| May 2020 | 43 counties + `explicatii.csv` | 4,249,671 | quoted CSV, lowercase header, 44 columns |
| June 2021 | 1 | 1,033,472 | as 2020 |
| June 2022 | 2 + tax code list | 2,358,009 | `^`, 61-column header, rows end with an extra `^`, Windows-1250 |
| June 2023 | 1 zip | 2,460,565 | as 2022 with `\|`, UTF-8 |
| June 2024 | 1 zip | 2,509,355 | as 2023 |
| June 2025 | 1 | 2,559,909 | as 2022 (`^`, Windows-1250) |
| July 2026 | 2 (A, B) | 2,647,766 | as 2022, UTF-8 |

Mureș was republished over older releases. The 2018 dataset's Mureș file (modified 15.07.2019) is byte-identical to the second Mureș resource of 2019 and its newest processing date is 11.07.2019: it is 2019 data. The first 2019 Mureș resource (modified 18.05.2020) is identical to the 2020 file. Each release imports only files whose content belongs to it: 2018 has no Mureș, 2019 uses the second resource (downloaded separately; both 2019 resources had the same download name). Row counts above are for the files each release uses; the profile JSON counts the files as published.

`explicatii.csv` names the 2018–2021 columns: the 2018 and 2019 files are the first 39 or 40 of them, in the same order.

## Coverage differs by release

- 2018–2020 include struck-off taxpayers (about 2 million `RADIERE` rows each); from 2021 they are left out. 2026 lists 80.
- 2021 has only 1.03M rows (the file is complete: 1,033,473 lines). Absence from a release therefore cannot mean a change.
- From 2022 each row has `TIP_CONTRIB`: about 1.8M `PJ` (legal entities) and 0.8M `PF` (individuals with a fiscal code: sole traders, liberal professions). Before 2022 the type is not given.
- `TIP_UNITATE` is "Sediu central" for 99.7%; the rest are branches, fiscal representatives and permanent establishments with their own fiscal code.

## Values

- CUI: valid checksum on every row except one blank in 2022. A CUI repeats within a release at most twice (31 in 2018, 20 in 2019, 3 in 2023).
- Dates: `03 Martie     2015` (Romanian month names, padded) and `01-01-2003` before 2022; `17.07.2025` with an optional time after.
- Tax obligations (profit tax 100, micro-enterprise tax 120, excise 200, social contributions 410–480, payroll tax 602, …): before 2022 each column is `NU` or the date the obligation was registered; from 2022 `DA` or `NU` (with a trailing space). The set grows from 18 to 33 codes in 2022. VAT follows the same pattern.
- Registry reference split into county (`J40`, `F12`), number and year; complete in about 55% of rows. The letter tells companies (J, C) from sole traders (F).
- Placeholders: `-` and `null` in text fields, `0` in phone and fax, postal codes that lost their leading zero (five digits, Bucharest).

## Malformed rows

- 2022–2026: one record per release is split over two lines (7 + 56 fields; 2022 also 2 + 61). The pieces add up to a whole record.
- 2023–2024: 300 and 314 rows have a `|` inside the street (or, 11 times, the name).
- 2021: six records at the end of the file have broken quoting (7–62 fields).
- 2022 and 2025: one line each with a byte that is not valid in UTF-8 or Windows-1250.

## Decisions for the import (S03.2)

- One row per CUI and release, kept as change-only periods like the registry history. Unlike the registry, a period continues across releases that miss the taxpayer, because coverage varies by release; the number of releases that saw it is kept.
- Compared between releases: name, taxpayer and unit type, county, locality, sector, street and number, phone, fax, registry reference, VAT, fiscal status and its date, the obligation set. Last declaration and processing dates, building details and registration dates of obligations come from the latest release.
- Individuals keep name, county, locality, sector and postal code only, no street, phone or fax. An individual is a `PF` in any 2022+ release, or a row with an `F` registry reference; a pre-2022 taxpayer with neither a `PJ` type nor a `J`/`C` reference is treated the same way.
- Split records are joined, the extra `|` is merged back into the street or name, unrecoverable records are counted and skipped. Lines are read as UTF-8 and fall back to Windows-1250.
