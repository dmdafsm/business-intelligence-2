# S02.3 Company statuses and authorized CAEN activities

Date: 2026-10-25. Branch: codex/registry. Formats: [S02.1 addendum](s02-1-registry-inspection.md#addendum-2026-10-25-statuses-and-authorized-activities). Operation: [registry import guide](../../docs/engineering/registry-import.md#statuses-and-activities).

## What was built

- `OD_STARE_FIRMA.CSV` and `OD_CAEN_AUTORIZAT.CSV` downloaded with the maintainer's approval and pinned in the catalog.
- Annex imports tied to the published OD_FIRME snapshot (same reference date required). They use the same hash check, one transaction and replay rules as the snapshot; a new snapshot deletes them.
- `registry.registration_statuses` (all current codes) and a `lifecycle` value per registration and company, chosen by explicit code lists in order of severity. The source has no status dates, so this is the current state, not a history.
- `registry.company_activities`: the current registration's activities in its newest CAEN version. Activities of older registrations and superseded versions are not kept.
- `VACUUM (ANALYZE)` after each registry import commits; without it the in-place updates doubled the table sizes with dead rows.

## Real-data run

| File | Result |
| --- | --- |
| OD_STARE_FIRMA.CSV | 4,670,796 rows (3 without registry number), all linked to a registration, no unknown codes; 189 s |
| OD_CAEN_AUTORIZAT.CSV | 19,746,934 rows (33 without registry number, 2 invalid codes), 18,918,633 company activities; 135 s |

- Companies by lifecycle: 1,787,181 active, 2,091,150 struck off, 139,445 suspended, 84,826 in dissolution or liquidation, 23,529 in insolvency, 273 other, 10 unknown.
- Companies by CAEN version of their activities: Rev.2 1,844,563; Rev.3 760,917; 2003 162,249; 1998 19,148; none 1,339,537 (mostly struck-off or old individual registrations).
- Spot check: CUI 381002 (two registrations, both struck off) shows StruckOff with both `radiată` codes.

## Tests

Lifecycle precedence (8 cases); PostgreSQL: statuses on the fixture snapshot (moved company active through its current registration, suspended individual, insolvent cooperative, unknown registration counted, replay), activities limited to the current registration's newest version with invalid codes counted, an annex of another snapshot refused, and a new snapshot removing previous annexes.

## Limits

- No main CAEN activity in the registry; it comes from the financial statements (S04).
- Grouping across Rev.2 and Rev.3 needs the ONRC correspondence table (S05).
- The status import updates 4.2M rows in place and takes about three minutes; fine for a monthly snapshot, worth revisiting if snapshots become frequent.
- Not run in Docker (raw files are not mounted into the container yet).
