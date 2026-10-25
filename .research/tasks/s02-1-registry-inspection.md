# S02.1 Registry formats inspection

Date: 2026-10-25. Files reused from the original repository's `.research/raw` (hard-linked into this repo's ignored `.research/raw/`); no new downloads. Profiling script: [inspect_onrc_firme.py](../tools/inspect_onrc_firme.py).

## Files

| File | Source | License | Bytes | SHA-256 |
| --- | --- | --- | --- | --- |
| OD_FIRME.CSV | [ONRC firme 19.10.2026](https://data.gov.ro/dataset/firme-19-10-2026) | CC BY 4.0 | 695,782,068 | e7f48934242c7bb1c3cfb77aa85d778a1857c4b065664fa593f64ade1733ec85 |
| N_CAEN.CSV | [ONRC nomenclatoare 19.10.2026](https://data.gov.ro/dataset/nomenclatoare-19-10-2026) | CC BY 4.0 | 276,396 | 45f207eb6634eea667c8450e20ac8197743ba7ec2371d19a269b20e24f07b34f |
| N_STARE_FIRMA.CSV | same | CC BY 4.0 | 12,740 | c3b4af92691fe6bb47e924e13070b80c605672225c399e1f48ef5b578beadf9b |
| N_VERSIUNE_CAEN.CSV | same | CC BY 4.0 | 90 | 3e4f61cf2c3696bc281997a44d5072acfbfe51b5c5a5ce74d3aaba5cdd499ef7 |
| siruta_s1_2024.csv (an XLSX workbook despite the name) | [INS SIRUTA S1 2024](https://data.gov.ro/ro/dataset/siruta-s1-2024) | CC BY 4.0 | 1,095,632 | 50ae2c83b73c921bec3d25a937702b39c7bb51581a81ec13fbe227f1ac5a876e |

Not downloaded yet from the same ONRC snapshot: `OD_STARE_FIRMA.CSV` (92 MB, statuses) and `OD_CAEN_AUTORIZAT.CSV` (437 MB, authorized activities). OD_FIRME has neither status nor CAEN, so grouping by CAEN and showing status need them.

## OD_FIRME.CSV

- UTF-8 with BOM, `^` delimiter, no quoting, CRLF line endings, 20 columns, 4,229,451 data rows, no width errors.
- Registration date: `dd/MM/yyyy` (3,920,863 rows) and, for recent registrations, `dd/MM/yyyy HH:mm:ss` (308,534 rows); 54 blank. Years 1990–2026.
- Registry number formats: the classic `J40/1234/1998` (also F/C prefixes) and the 2024+ format `J2024004913234`. EUID is `ROONRC.` + registry number on all but 66 rows.
- Legal forms: SRL 2.80M, PFA 739k, II 251k, PF 191k, AF 121k, IF 57k, SA 34k, SNC 28k, cooperatives (CA, SC, OC*) ~10k, RA 1.3k, others small; 41 blank, 1,885 `N/A`.
- CUI: 4,020,543 valid checksums, 86,413 zero, 267 bad checksum, 14 blank, 2 too short. **122,212 values have 11–13 digits: these are personal numeric codes (CNP) of PF/AF/PFA holders, not CUIs. They must never be stored.**
- Old-style Romanian letters with cedilla (ş, ţ) are used in 3.0M rows (for example `Bucureşti`, `Timiş`); matching must fold them with the comma forms (ș, ț).
- County: 42 values plus 158 blank. Locality text has shapes `Sat X, Comuna Y` / `Loc. X, Oraş Y` (1.51M), a single UAT or locality (1.94M), `Bucureşti Sectorul N` (777k), the placeholder `***localitatea negasita***` (2,341) and blank (174).
- WEB filled on 11,616 rows; TARA_FIRMA_MAMA on 11,154.

## Identity findings

- 4,224,674 distinct registry numbers. 4,714 occur on more than one row; for 3,553 the rows differ, mostly in postal code, CUI (one row zero, the other valid) or street. Rows are never adjacent. Rule: merge rows per registry number, prefer a valid CUI, take the most frequent value per field and record which fields disagreed.
- 88,762 valid CUIs belong to more than one registry number (up to 6). Examples show the same company re-registered after moving county (`J40/…` then `J23/…`) or renumbered in the 2024 format. **The company is the CUI; registry numbers are its registration history.** Registry entries without a valid CUI stay companies of their own, keyed by registry number.

## Nomenclatures and SIRUTA

- N_CAEN: `^`-delimited, BOM, columns SECTIUNEA, SUBSECTIUNEA, DIVIZIUNEA, GRUPA, CLASA, DENUMIRE, VERSIUNE_CAEN. Four versions (0 = 1998, 1 = 2003, 2 = 2008 / Rev.2, 3 = 2025 / Rev.3); 615 Rev.2 and 651 Rev.3 classes. The original repo found 17 version-0 classes with blank names.
- N_STARE_FIRMA: 197 status codes with names.
- SIRUTA workbook: one sheet `Data`, columns SIRUTA, DENLOC, CODP, JUD, SIRSUP, TIP, NIV, MED, REGIUNE, FSJ, FSL. Level 1 counties, 2 UATs, 3 localities. The original repo matched 99.89% of registry seats to SIRUTA by parsing the locality structure and matching the UAT first.

## Addendum 2026-10-25: statuses and authorized activities

Downloaded with the maintainer's approval from the same ONRC 19.10.2026 dataset (sizes match the CKAN metadata):

| File | Bytes | SHA-256 |
| --- | --- | --- |
| OD_STARE_FIRMA.CSV | 92,172,215 | bcac743ef1de99424018507f3ffca05d821c06fb240bbb4d5e8341e047a97407 |
| OD_CAEN_AUTORIZAT.CSV | 437,467,917 | 05db43ffc8a3e5179332c53c0f7d22a5845341506292b7463e42d74747b9440d |

Profiling script: [inspect_delimited.py](../tools/inspect_delimited.py).

### OD_STARE_FIRMA.CSV

- UTF-8 BOM, `^`, columns `COD_INMATRICULARE^COD`; 4,670,796 rows covering 4,224,663 registry numbers (up to 15 codes each), no exact duplicates, every code in N_STARE_FIRMA (120 codes used).
- There are no dates: the file lists the **current** set of status codes per registration, not a history.
- Codes 1xxx describe the lifecycle (1048 funcțiune 1.79M, 1084 radiată 2.19M, 1074 întrerupere temporară 143k, 1049 dizolvare 60k, 1052 lichidare 16k, 1070 faliment 16k, 1107 insolvență 5k, 1139/1083 under the insolvency laws 22k…). Codes 2xxx are mentions added to the record (2069 sediu expirat 254k, 2120 suspendare expirată 101k, 2065 mandat administratori expirat 30k…).
- Common combinations: funcțiune + sediu expirat (178k), întrerupere + suspendare expirată (67k), faliment + Legea 85/2014 (9k). 1,653 struck-off registrations also carry mentions.

### OD_CAEN_AUTORIZAT.CSV

- UTF-8 BOM, `^`, columns `COD_INMATRICULARE^COD_CAEN_AUTORIZAT^VER_CAEN_AUTORIZAT`; 19,746,934 rows for 2,861,095 registry numbers (up to 627 rows each; registrations without activities are absent).
- No column marks the main activity. Main CAEN must come from the MF financial statements (S04).
- CAEN versions per registration: only Rev.2 1.88M, only Rev.3 738k, only 2003 167k, Rev.2 and Rev.3 together 22k, older mixes 56k. Registrations are being moved to Rev.3 (2025) gradually, so grouping by CAEN must bridge Rev.2 and Rev.3 (ONRC publishes a correspondence table).
- 139 codes have 2–3 digits (group or division level in old versions); 132 codes are not in N_CAEN for their version.
