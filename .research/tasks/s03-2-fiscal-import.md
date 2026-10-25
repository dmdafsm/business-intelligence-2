# S03.2 MF identification releases as taxpayer periods

Date: 2026-10-25. Branch: codex/registry. Formats: [inspection note](s03-1-fiscal-inspection.md). Operation and results: [fiscal import guide](../../docs/engineering/fiscal-import.md).

## What was built

- Fiscal module: `fiscal.taxpayer_periods` (change-only periods per CUI), `fiscal.releases` (applied releases with their import report), `fiscal.taxpayer_kinds` (PJ/PF from the 2022+ releases). Migration `FiscalHistory` only creates these tables.
- `MfIdentificationReader` for the three layouts, with per-line UTF-8/Windows-1250 decoding, split-record joining, separator repair, five date formats and placeholder rules.
- `fiscal import-all <raw-root> [--rebuild]`, `fiscal status`, `fiscal show <cui>`.
- 135 files pinned in the source catalog; the two Mureș files that hold another year's data are left out (see the inspection note).
- Fixed in shared code: `VerifiedFileStream.Open` reported a wrong-size file as "Cannot access a closed file" (it read the length after closing the stream).

## Real-data run

9 releases in 621 s, 7,970,233 periods for 5,252,927 taxpayers, 3.3 GB. No individual has a stored street, phone or fax (checked with a query over all periods). 3,864,764 taxpayers match an ONRC company by CUI. Example: CUI 35 (IMECO SA) has one period 2018–2026 covering the 8 releases that list it (it is missing from 2021).

## Problems found while running

Each was fixed, tested and the full import rerun:
- 2021 quotes its header line.
- 2023 and 2024 write dates as `14/Jul/23` and `14-Jul-2024 8:19:39 AM`; unparsed, they broke every period.
- 2024 lost diacritics (U+FFFD): keys now let a lost letter match any one letter, including inside "PERSOANĂ FIZICĂ AUTORIZATĂ", which the 2024 file spells out. Without this, 2024 and 2025 each opened over 2 million periods; now about 0.5 million.
- 2025 leaves the registry reference blank for 300k more taxpayers than 2024; a blank reference no longer counts as a change.

## Tests

Headerless CSV by position (quoted values, dates, flags with registration dates, placeholders, postal codes, invalid CUI and short record counted); delimited records (split line joined, extra separator merged, Windows-1250 line, 2023/2024 date formats); PostgreSQL: periods over three releases in three layouts including a zip (respelling and a missing release keep the period, phone change opens one, individuals inferred from a later release or an F reference keep no contacts, lost letters keep the earlier spelling), replay no-op, older release refused, rebuild clears; a changed file is refused before anything is written.

## Limits

- Values are attributed to releases (period start and end dates), not to individual files; within a release a CUI comes from one file.
- Before 2022 the taxpayer type is inferred; about 16% of those taxpayers stay unknown and lose street and phone like individuals, including some legal entities that disappeared before 2022.
- Names are compared after folding; a period keeps the latest intact spelling, so it shows how the taxpayer was named at the end of the period.
- 2018 has no Mureș and 2019 no Maramureș; 2021 covers only 1.03M taxpayers. Missing is never read as a change.
- The SIRUTA codes of the fiscal address are not resolved; grouping uses the registry geography (S05).
- Not run in Docker (raw files are not mounted into the container yet).
