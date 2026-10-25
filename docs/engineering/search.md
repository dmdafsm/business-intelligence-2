# Search and grouping

The home page finds companies by name, CUI or registry number and groups them by lifecycle, county, locality (UAT) and CAEN code. It reads the `search` schema, which the Search module rebuilds from the published registry snapshot.

## Building the index

```sh
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll search build
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll search status
```

Run `search build` after the registry imports (firms, statuses, activities, history). It replaces every search table in one transaction, so pages keep the previous build until it commits, then vacuums them. Rerunning it gives the same tables. Until the first build, searches show that no data is imported.

| Table | Contents |
| --- | --- |
| `search.companies` | One row per company of the published snapshot: identifiers, name key, lifecycle, SIRUTA codes and CAEN tokens |
| `search.names` | Current names and the other names the company's registrations had in earlier snapshots |
| `search.registration_numbers` | Every registry number of the company's registrations, normalized |
| `search.counts` | Companies per county × lifecycle × CAEN code, with "any" rows for each dimension |
| `search.builds` | One row per build with its report |

## Rules

- **Identifiers first.** A valid CUI (with or without `RO`, spaces ignored) or a registry number matches exactly. Old-form numbers are compared without leading zeros (`J08/0005/2010` = `J8/5/2010`); new-form numbers (`J2024000100123`) as written. A personal numeric code is never a CUI and finds nothing.
- **Then names, in tiers:** equal to the query, starting with it, a later word starting with it, every query word starting a word of the name. Within a tier, current names come before earlier names and active companies first. Names are folded (case, diacritics, punctuation); `S.R.L.`/`SRL` and similar spellings are unified, a leading `SC`/`S.C.` is ignored, and legal-form words alone (SRL, SA, PFA…) do not narrow a search.
- **Approximate names** (pg_trgm word similarity ≥ 0.5 of the query words) are added only when the tiers above find fewer than a page, at most the 100 closest. One wrong letter in a word matches; two in a short query usually do not.
- An earlier name is shown under the result ("Denumire anterioară").
- Name queries need two letters or digits. Pages are 20 results; at most 250 pages are served.
- **Filters combine** and live in the URL (`q`, `judet`, `localitate`, `caen`, `rev`, `stare`, `pagina`), so every state works without JavaScript and can be linked. County and CAEN filters drill down: county → its UATs, section → division → group → class. The lifecycle counts ignore the lifecycle filter so the alternatives stay visible.
- **CAEN** groups the activities authorized at ONRC, in the CAEN version of each company's registration (Rev.3 from 2025 or Rev.2). A company appears under each of its activities, so group counts do not add up to the total. Companies still on Rev.2 appear only in the Rev.2 tree; there is no Rev.2 ↔ Rev.3 correspondence yet. An unknown code is named on the page and not applied.
- Browsing by county, CAEN and lifecycle reads `search.counts`; a name query or a locality is counted live over the matching companies (collected once per request into a temporary table).

## Measured on the full registry

Build: 196 s, 300 MB peak memory, 4,126,414 companies, 70,410 earlier names, 4,224,673 registry numbers, 456,014 count rows (Apple M-series laptop, local PostgreSQL 18).

Warm response times of the whole page (median of three after a first request), through the local portal:

| Request | Results | Time |
| --- | ---: | ---: |
| Home page (counties, CAEN sections) | — | 3 ms |
| CUI `2816464` / registry number `J04/1862/1994` | 1 | 5 ms |
| Name `dedeman` / `banca transilvania` | 104 / 64 | 27 / 16 ms |
| Typo `dedmn` (approximate tier) | 100 | 12 ms |
| Broad word `construct` / `com` | 101,418 / 211,383 | 0.59 / 0.74 s |
| `construct` in Iași county | 3,470 | 0.29 s |
| County Cluj; active companies in CAEN section K | 204,081 / 106,598 | 9 / 4 ms |
| Bucharest, CAEN 6210 | 22,950 | 34 ms |
| Locality Cluj-Napoca (live counts) | 119,491 | 0.25 s |
| Locality București (its only UAT), with CAEN 62 | 742,568 / 30,347 | 1.26 / 0.14 s |
| Page 250 of active companies | 1,787,181 | 6 ms |

Broad words and very large localities are the slow cases: their counts are computed over every match.
