# S05 Search and grouping

Status: completed. Depends on S02. Part of the [MVP plan](../mvp-plan.md).

## Outcome

Users find companies by name (fuzzy), CUI or registry number, and browse or filter by CAEN section/division/class, county and city/UAT, with paginated results and filters in the URL.

## Acceptance

- Exact identifiers match first, then name prefix, then approximate name matches.
- Filters combine; counts are shown per group; results are paginated server-side.
- Works without JavaScript; a small script may add suggestions as you type.
- Measured response times on the full national dataset are recorded.

## Tasks

- S05.1 Search index, search and grouping page — [note](../tasks/s05-1-search.md). Operation: [search guide](../../docs/engineering/search.md).
- S05.2 Faster filters, legal form filter, Excel export, form layout — [note](../tasks/s05-2-search-filters.md).

Open: CAEN Rev.2 ↔ Rev.3 bridging in the activity filter. The INS correspondence table is imported since S10.2, but it is used only for financial statements.
