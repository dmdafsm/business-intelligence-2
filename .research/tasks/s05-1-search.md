# S05.1 Search by name, CUI and registry number; grouping by CAEN, county and city

Date: 2026-10-25. Branch: codex/registry. Operation and rules: [search guide](../../docs/engineering/search.md).

## What was built

- Search module: `search.companies`, `search.names` (current and earlier names), `search.registration_numbers`, `search.counts` (county × lifecycle × CAEN, with "any" rows) and `search.builds`. Migration `SearchIndex` creates them and enables `pg_trgm`.
- `search build` rebuilds all of it from the published registry snapshot in one transaction (pages keep the previous build until commit); `search status` prints the current build.
- `CompanySearch`: identifiers first, then name tiers (equal, prefix, later word, all words), approximate names only when fewer than a page match; filters by county, UAT, CAEN (section/division/group/class, Rev.3 or Rev.2) and lifecycle; counts per group; 20 results per page, 250 pages at most.
- Home page: search box, counties and Rev.3 sections with counts; results page with removable filter chips, filter groups (lifecycle chips, county/locality and CAEN lists with a disclosure for long lists, CAEN path) and pagination. Works without JavaScript; on phones results come first with a link to the filters.
- New components `md-status`, `md-pagination`, input chips and chip counts, added to `/Design`; icons close, chevron-left and filter-list.

## Real-data run

`search build` on the scratch database: 196 s, 300 MB peak, 4,126,414 companies, 70,410 earlier names, 4,224,673 registry numbers, 456,014 count rows, no activity code outside the nomenclature. Response times are in the search guide: identifiers 5 ms, typical names under 30 ms, stored browsing under 40 ms, broad words up to 0.74 s, the Bucharest locality 1.26 s.

Found while running it and fixed: the first ranking used substring matches plus trigram similarity of the whole name, so "construct" took 1.5 s and "dedeman srl" matched 18,000 names through "SRL"; legal-form-only queries ("srl") took 16 s. Words now match at word starts, legal-form words are ignored, approximate matching runs only when few names match, and the page joins only its 20 rows.

## Tests

- Unit: CUI parsing (RO prefix, invalid check digit, personal code), registry number normalization, name keys, place names.
- PostgreSQL: not ready before a build; CUI, padded old registry number (SQL and .NET keys agree) and personal-code queries; tier order, active first, earlier names, one-typo approximate match, short query; stored and live counts agree; filters combine; Rev.2 tree separate; unknown CAEN code; pagination; rebuild gives the same counts.
- Browser (Chromium): search and filter without JavaScript, CAEN drill-down and version switch, unknown code notice, CUI and earlier-name searches, pagination, phone layout without horizontal scroll.

## Limits

- No Rev.2 ↔ Rev.3 correspondence: companies still registered under Rev.2 appear only in the Rev.2 tree. The ONRC correspondence table is a PDF with split classes; bridging needs its download and a mapping decision.
- CAEN groups use authorized activities (ONRC marks no main one); a company counts under each of them.
- Bucharest is one UAT, so its locality filter adds nothing; selecting it is counted live (1.26 s).
- Results do not link to a profile yet (S06).
- Two typos in a short query usually find nothing.
- Not run in Docker.
