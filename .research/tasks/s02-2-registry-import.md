# S02.2 Import nomenclatures, SIRUTA and the latest OD_FIRME snapshot

Date: 2026-10-25. Branch: codex/registry (stacked on codex/foundation). Formats: [S02.1](s02-1-registry-inspection.md). Operation: [registry import guide](../../docs/engineering/registry-import.md).

## What was built

- Pinned source catalog embedded in the Modules assembly; imports verify size and SHA-256 while streaming and record attribution in `registry.source_files`.
- Tables in schema `registry`: `caen`, `status_codes`, `siruta`, `registrations`, `companies`, `source_files`, `published_datasets`. Columns use snake_case for hand-written SQL.
- `registry import|status|show` operator commands.
- Decisions taken from the inspection:
  - The company key is the valid CUI; registry numbers are its registrations. Without a valid CUI, the registration is its own company.
  - Rows sharing a registry number are merged (valid CUI over zero, most frequent value otherwise, disagreements listed). Two different valid CUIs leave the CUI empty (`Conflict`), so nothing is joined on a guess. The original repository quarantined such rows instead, hiding the companies.
  - Personal numeric codes in the CUI column are dropped in the reader. Individuals keep only a coarse address, as in the original repository.
  - The 1998/2003 CAEN versions contain a source defect (Agriculture labelled section D in version 1). It is reported in the import report, not fixed by guessing.

## Real-data run (19.10.2026 snapshot)

Local PostgreSQL 18 on macOS, Release build:

| File | Result |
| --- | --- |
| N_CAEN.CSV | 3,739 entries, 2,343 classes (17 without a name), versions 0–3, 1 source defect |
| N_STARE_FIRMA.CSV | 197 codes |
| SIRUTA S1 2024 | 16,978 places: 42 counties, 3,181 UATs, 13,755 localities |
| OD_FIRME.CSV | 4,229,451 rows (65 without registry number) → 4,224,673 registrations → 4,126,414 companies, in 65 s, about 300 MB peak memory |

- CUI states: 4,017,958 valid, 122,210 personal codes dropped, 84,185 zero, 256 bad checksum, 62 conflicting, 2 too short.
- Companies by form: 2,763,194 companies, 1,351,980 individuals, 9,274 cooperatives, 1,966 other. 88,757 companies have 2–7 registrations.
- Geography: 45.85% resolved to a UAT, 35.62% to a locality, 18.40% to a Bucharest sector; 0.13% unresolved (placeholder locality, unmatched UAT, 146 without county).
- Rerunning a file reports `AlreadyImported`; a first attempt failed on a closed stream (fixed) and left nothing published.
- Spot checks: CUI 381002 groups J40/13703/1991 and J23/1108/2012 (current: J23, Ilfov); CUI 3474203 groups J40/5594/1992 and J2024004913234.

## Tests

- Unit: CUI classification, CAEN levels and defect reporting, status duplicates, OD_FIRME reader (trimming, both date formats, personal codes dropped, missing registry number, bad header), SIRUTA matcher shapes (village/commune, comma and cedilla letters, bare UAT, sector, unknown locality, placeholder, unknown county), XLSX reading.
- PostgreSQL: merge and grouping, conflicting CUIs, individual privacy (the personal code appears nowhere in the table), geography, reimport no-op, a new snapshot replacing the old, a tampered file changing nothing, SIRUTA required first, SIRUTA import and replay.

## Limits

- No status or CAEN activity yet (S02.3 needs `OD_STARE_FIRMA.CSV`, 92 MB, and `OD_CAEN_AUTORIZAT.CSV`, 437 MB).
- Only the latest snapshot; no history (S02.4).
- Not run in Docker: the raw files are not mounted into the container yet.
- The current registration is the one with the latest registration date; statuses (S02.3) will show whether older ones were struck off.
