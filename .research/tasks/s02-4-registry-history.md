# S02.4 Registry history from older snapshots

Date: 2026-10-25. Branch: codex/registry. Formats: [inspection note](s02-4-history-inspection.md). Operation: [registry import guide](../../docs/engineering/registry-import.md#history-from-older-snapshots).

## What was built

- The 95 yearly snapshot files (2013–2025) pinned in the source catalog by size and SHA-256, with year, family and local path.
- `LegacySnapshotReader` for the 2013–2024 layouts: both separators, optional and repeated headers, quoted values, trailing empty fields, `|` inside addresses (merged into the last column), records that continue on the next line, and truncated UTF-8 (read with a replacement character). Free-text addresses are split into county and locality.
- `registry.registration_periods`: change-only periods per registry number (name key, lifecycle, CUI, place), with the status codes, address and SIRUTA codes of the latest snapshot in the period. `registry.history_snapshots` records each applied snapshot with its counts.
- `registry history <raw-root> [--rebuild]`: applies snapshots oldest first, one transaction each, skips applied ones, refuses an older one, vacuums after each. `--rebuild` truncates and replays everything.

## Real-data run

14 snapshots, 669 s (21–75 s each), about 730 MB peak memory. 6,633,773 periods for 4,862,052 registry numbers (1.36 each; 3.49M with one period, up to 11). The table takes 3.1 GB with its indexes.

| Snapshot | Registrations | Extended | Opened |
| --- | --- | --- | --- |
| 21.10.2013 | 2,463,214 | 0 | 2,463,214 |
| 31.10.2016 | 2,802,337 | 2,526,107 | 276,230 |
| 31.10.2017 | 2,941,709 | 2,661,414 | 280,295 |
| 18.12.2024 | 3,963,607 | 3,597,220 | 366,387 |
| 08.12.2025 | 4,111,575 | 3,519,247 | 592,328 |
| 19.10.2026 | 4,224,673 | 3,700,328 | 524,345 |

- 2017–2020 joined 1,816–3,987 continuation lines per year; 2017 merged 241 addresses containing `|`.
- The 2025 jump in opened periods is real change, not a layout artefact: of the 122k registrations that changed between 2024 and 2025, 119k changed only lifecycle (44k active to struck off, 25k active to dissolution, 20k dissolution to struck off); name-only changes are 633.
- Spot check: CUI 381002 shows J23/1108/2012 active in Petreşti 2013–2015, suspended 2016–2017, struck off from 2018 (locality respelled "Sat Petreşti, Comuna Corbeanca" in the same period), and J40/13703/1991 struck off throughout.

## Problems found while running

- The first full run hung for two hours on 2016: the anti-join that found registrations without an extended period was planned from statistics that could not see rows updated in the same transaction. Extended registrations are now flagged in the staging table instead.
- One vacuum at the end left the table at 6.4 GB; vacuuming after each snapshot and truncating on rebuild halved it.

## Tests

Free-text address splitting and name keys (cases); PostgreSQL: legacy layouts joined, trimmed and unquoted; periods across three snapshots (respelling keeps the period, status change and move open new ones, missing registration not read as struck off, sole traders without address or CUI), replay is a no-op, an older snapshot is refused, rebuild clears.

## Limits

- The legacy files have no legal form or registration date, so periods do not track legal-form changes (the step plan listed them).
- Lifecycle in 2025–2026 comes from `OD_STARE_FIRMA`, before that from inline codes; the code lists are the same.
- Between snapshots the exact change date is unknown: a period starts at the first snapshot that shows it.
- Gaps (a registry number missing from a year) are not recorded separately; the period just ends at the last snapshot that had it.
- Not run in Docker at the time; the full import later ran in Docker ([REPO Docker stack](repo-docker-stack.md)).
