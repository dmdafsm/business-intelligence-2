# S08.6 Public procurement (SEAP) 2007–2026

Date: 2026-10-25. Branch: codex/representatives. Operation: [procurement import guide](../../docs/engineering/procurement-import.md).

## What was built

- 123 more SEAP files pinned (2007–2024 and 2026; there is no 2019 dataset), downloaded with the maintainer's approval; download lists under `.research/`.
- `SeapRows`: header aliases across years, CSV with delimiter detection, `|` quoting and per-line UTF-8/Windows-1250 decoding, all sheets of .xlsx, .ods (`StreamingOds`) and old .xls, realignment of rows with unquoted commas, removal of invalid characters. Parser `seap-v2`.
- `awarded_on` column with a `(winner_cui, awarded_on)` index and `procurement.year_totals` (migration `ProcurementTotals`), rebuilt by `SummarizeProcurement` after `import-all` or by `procurement summarize`. The profile reads totals and the most recent wins from them instead of aggregating millions of rows.
- Totals keep a missing value missing: a year with only blank values shows "—" plus how many awards have no published value, never 0.
- `registry history` and `fiscal import-all` reruns report applied snapshots/releases as `AlreadyImported` instead of refusing, so `scripts/docker_import_all.py` is rerunnable.
- `database migrate` allows 30 minutes per command for index builds on the national tables.

## Real-data run

131 files, 37,245,934 rows (4.79 million contracts, 32.46 million direct purchases), 11 GB, 511 s locally, 2.47 GB peak. 38,970 rows of 2016 H1 realigned, 4 skipped. Before the totals, the page of the largest supplier (595,000 rows) took 2 s warm and 8 s cold.

Docker (2026-10-26, `python3 scripts/docker_import_all.py` on the stack with existing data): migration applied on start; procurement 703 s including 105 s summarize (1,024,883 year totals), search build 290 s, whole run 17 min; earlier steps AlreadyImported. Profile pages on :8090: 28–508 ms. Postgres 1.8 of 3 GB.

## Commands

`python3 scripts/verify.py` (with `LC_ALL=en_US.UTF-8 TMPDIR=/tmp/` locally): 114 backend and 24 browser tests pass. Browser check of a profile with blank values on desktop and 375 px.

## Tests

Old caret CSV with Windows-1250 lines, `|`-quoted CSV with slash dates, realigned and skipped comma rows, .ods sheets; year totals count a contract once; coverage text for whole and partial years.

## Limits

See the import guide: 2024 Q1 contracts from the .xls, 2022 Q2 appended rows skipped, blank framework values kept missing.
