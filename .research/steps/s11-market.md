# S11 Market statistics

Status: completed. Depends on S02 and S05. Added on 2026-10-26 at the maintainer's request, carried over from the original repository (F07). Insolvency and published tax debts were researched first: no open bulk source exists (see Sources), so this step came next.

## Outcome

A public "Statistici" page shows how many companies are registered each month and year, how many of each year's registrations are still active, the current registry by state, and the state changes seen between the yearly registry snapshots. Everything can be narrowed by county and by kind of entity (companies, PFA/II/IF, cooperatives).

## Sources

- ONRC OD_FIRME, latest snapshot: registration date, county, legal form, current state of every registered entity, struck-off ones included.
- ONRC yearly snapshots 2013–2026 (registry history from S02.4): the state of each registration in each snapshot.
- Not available as open data (researched 2026-10-26):
  - ANAF published tax debts: [anaf.ro/restante](https://www.anaf.ro/restante/) is an interactive form only. data.gov.ro has a single list as of 31.03.2016 with no license stated.
  - The insolvency bulletin (BPI) is a paid ONRC service. The free part is an interactive search. The insolvency state itself is already in OD_FIRME and on the profile.

## Rules

- ONRC publishes no date of striking off, dissolution or insolvency. A change of state is shown only as "seen between snapshot A and snapshot B", never as an event date.
- The month of the snapshot is incomplete and is labeled as such; the chart shows complete months only.
- A registration without a county is counted under "județ necunoscut", never dropped.

## Acceptance

- Totals per year equal the number of registry entities with that first registration year under the same filters.
- Filters live in the URL; the page works without JavaScript and on a phone.
- `market build` rebuilds the tables in one transaction and runs after the ONRC import job.

## Tasks

- S11.1 Market statistics tables and page — [note](../tasks/s11-1-market-statistics.md).
