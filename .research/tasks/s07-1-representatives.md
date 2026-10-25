# S07.1 ONRC representatives on the company profile

Date: 2026-10-25. Branch: codex/representatives. Decision: [ADR 0001](../../docs/engineering/decisions/0001-representatives-personal-data.md). Format inspection: done in the original repository (D08.1), confirmed here on the same files.

## What was built

- `OD_REPREZENTANTI_LEGALI.CSV` and `OD_REPREZENTANTI_IF.CSV` (19.10.2026) copied from the original repository's raw folder (no download; SHA-256 equal to its inspection) and pinned in the catalog.
- `registry.representatives`: every column of both files per snapshot, with person kind (natural, legal, unknown); imported as annexes of the OD_FIRME snapshot, removed with it when a newer snapshot is published. Migration `Representatives`.
- Profile section "Persoane": name, role, birth year for natural persons, place for firms; implausible birth years hidden; note that shareholders and beneficial owners are not open data. The query never selects full birth dates or places of birth.
- Import steps added to `scripts/docker_import_all.py`.

## Real-data run (Docker)

Legal file: 3,689,221 rows → 3,685,803 representatives in 36 s (3 without registry number, 3,415 identical repeats, 0 unknown registrations, 0 unreadable dates); natural 3,214,000, legal 310,829, unknown 160,974. Family file: 123,398 members in 6 s. 400 birth dates outside 1900–2026, not shown. Checked on the DEDEMAN SRL profile in the browser.

## Tests

PostgreSQL: all columns stored, kinds, identical rows merged, unreadable date kept as text, unknown registration counted, family file without address, removal with a newer snapshot; the profile query exposes only name, role, birth year and a firm's place. Browser: the section shows the birth year, not the date or birthplace, and a firm's place.

## Limits

- Only the current snapshot: no history of who ran a company before.
- People are not reconciled across companies; homonyms are possible.
- No correction or erasure procedure; the source is the authority.
- Shareholders and beneficial owners: not open data.
