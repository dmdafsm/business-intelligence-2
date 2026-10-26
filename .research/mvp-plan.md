# MVP plan

Draft, 2026-10-25. Spin-off of `business-intelligence`: the same stack (.NET 10, Razor Pages, PostgreSQL, modular monolith), engineering rules, guardrails and agent setup, with a smaller scope and a Material 3 UI built in-house.

## Goal

Search for a Romanian company and see everything public about it: general information, contact details, activity, status and, most importantly, its full financial history. Find companies by name, CUI or registry number, and browse or filter them by CAEN code, county and city.

Not in the MVP: reports and statistics, filters on employees/revenue/profit, administration and scheduled updates, accounts and portfolios, exports, AI, risk. Data is imported once, in bulk, from command-line tools. The update module was added after the MVP as S09 ([import jobs](steps/s09-import-jobs.md)): scheduled discovery and import of new editions, with an operator page. Derived financial indicators with a CAEN benchmark on the profile were added as S10 ([indicators](steps/s10-indicators.md)), and a public statistics page as S11 ([market](steps/s11-market.md)).

## Data sources

All of these are bulk open-data files on data.gov.ro. None is called live when someone views a page.

| Source | What it gives the profile | History available |
| --- | --- | --- |
| ONRC `OD_FIRME` | name, CUI, registry number, EUID, legal form, registered office, website, foreign parent's country | periodic snapshots 2013–2026, giving name and address changes |
| ONRC `OD_STARE_FIRMA` | status (active, in dissolution, struck off, …) | status history |
| ONRC `OD_CAEN_AUTORIZAT` | authorized activities | per snapshot |
| ONRC nomenclatures, CAEN Rev.2/Rev.3 correspondence | CAEN and status labels | CAEN version changes |
| INS SIRUTA | county / city / commune (UAT) codes for geographic grouping | — |
| MF `date_identificare_platitori` | fiscal address, phone, fax, VAT payer, fiscal status, registered tax obligations | yearly 2018–2026 |
| MF `situatii_financiare` | yearly financial statements: revenue, profit/loss, employees, assets, equity, debts, main CAEN | every year 2008–2025, plus the 2024 revision |

Optional, after the core works: ONRC legal representatives (personal data, so it needs a decision) and SEAP public procurement (already built in the old repo).

The old repo imported only one financial form family (`WEB_UU_AN`) for 2024–2025. The MVP covers every year and every form family that applies to companies, with a mapping from each year's dictionary to one shared set of indicators.

## What changes compared to the old repo

- **Join by CUI directly.** The old repo required an explicit, manual identity confirmation per ONRC–MF pair before it would show financials, so in practice almost nothing was shown. The MVP links records automatically when the CUI is valid, and flags ambiguous cases instead of hiding them.
- **One simple source catalog instead of per-use permission gates.** We keep license and attribution per source and show them on the profile. We drop the Retrieve/Store/Display/Export permission checks that every read went through.
- **Lighter evidence.** One task note per step, without a separate evidence JSON for every slice. `verify.py`, tests on real PostgreSQL and browser tests stay.
- **We keep:** imports that can be rerun and resumed, streaming with bounded memory, atomic publication, keeping blank, zero and missing distinct, and the source and period behind every value.

## Steps

1. **Foundation.** Copy the stack, `verify.py`, hooks, CI, Docker and the general engineering docs. Adapt `AGENTS.md` and `check_repository.py` to the new plan IDs. Build the Material 3 base: color, type, shape and elevation tokens for light and dark themes, plus an app shell with a top app bar, navigation rail and search field.
2. **Registry.** Import the latest ONRC snapshot (firms, status, CAEN activities, nomenclatures) and SIRUTA, then the older snapshots as history.
3. **Fiscal data and contacts.** Import every yearly MF identification file.
4. **Financials.** Inspect each year's files and dictionaries, map them to the shared indicators, and import 2008–2025.
5. **Search and grouping.** Search by name (fuzzy), CUI and registry number. Filter and browse by CAEN section, division and class, by county and by city/UAT.
6. **Company profile.** Sections for general information, contacts, address, activities, status history, and financial history as a table and charts (Material 3 cards, tabs and data tables), with the source and period of each value.

Each step ends with `verify.py` passing and a browser check of what users see.

## Open decisions

- Whether to show legal representatives.
- Whether SEAP procurement is part of the MVP.
- Downloads: the full import means downloading roughly 18 years of financial files, 9 MF identification files and the ONRC snapshots from data.gov.ro. I'll prepare a list with sizes for one approval. Files already in the old repo's `.research/raw` get reused.
