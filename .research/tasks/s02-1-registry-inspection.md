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
