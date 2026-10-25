# S02.4 Older registry snapshots: inspection

Date: 2026-10-25. Downloaded with the maintainer's approval: one snapshot per year 2013–2025, company files only (95 files, 7.2 GB), listed in [onrc-history-download-manifest.json](../onrc-history-download-manifest.json); size and SHA-256 per file in [onrc-history-downloaded.json](../onrc-history-downloaded.json). Profile: [onrc-history-inspection.json](../onrc-history-inspection.json), made with [inspect_onrc_history.py](../tools/inspect_onrc_history.py). The 19.10.2026 snapshot is the one already imported (S02.2, S02.3).

## Layouts

| Snapshot | Separator | Columns | Notes |
| --- | --- | --- | --- |
| 21.10.2013 | `\|` | name, CUI, registry number, status codes, county, locality | no header |
| 31.10.2014–31.10.2016 | `\|` | same, with header | 2015 drops the locality column in the no-office files; 2016 quotes every value |
| 31.10.2017 | `\|` | adds EUID; one free-text `ADRESA` replaces county and locality | 241 addresses contain `\|` |
| 01.11.2018–08.12.2020 | `^` | as 2017 | |
| 09.12.2021–18.12.2024 | `^` | free-text address plus structured `ADR_*` columns | most rows end with an extra empty field |
| 08.12.2025 | `^` | current `OD_FIRME` layout with `OD_STARE_FIRMA` | |

Common to 2013–2024:
- Separate files for active (`neradiate`) and struck-off (`radiate`) registrations, with and without a registered office; large files are split into parts that end on a complete line, and later parts may or may not repeat the header.
- Status codes are inline (`1048,2069`), so every snapshot carries the status at that date: this is the status history the current files lack.
- No registration date and no legal form.
- Addresses sometimes contain line breaks: the record's last field continues on the next line. 2022–2024 contain 1–75 fields cut inside a multi-byte character by the publisher; they are read with a replacement character.
- The status nomenclature files are Windows-1250 from 2018; the current N_STARE_FIRMA is used instead.
- CUI column: 12–13-digit personal codes appear in the struck-off files (about 122,000 per snapshot), as in the current file.

## Sizes

| Snapshot | Rows | Registry numbers |
| --- | --- | --- |
| 2013 | 2,468,025 | 2,463,602 |
| 2016 | 2,807,236 | 2,802,621 |
| 2019 | 3,233,686 | 3,229,014 |
| 2022 | 3,648,741 | 3,644,068 |
| 2024 | 3,973,200 | 3,963,679 |

After the reading rules, only 2 rows a year (2013–2016) remain malformed, plus the 241 2017 addresses with a `|`, whose extra pieces are joined back into the address.
