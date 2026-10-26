# S12 Prospecting filters

Status: completed. Depends on S04 and S05. Added on 2026-10-26 at the maintainer's request, carried over from the original repository (F05).

## Outcome

The company search narrows any result (name, county, locality, CAEN, state) by turnover and average employees, and orders it by either, so a user can list, for example, the largest companies of a CAEN class in a county.

## Rules

- The filters use only the latest published financial year, the same for every company and named on the page. A company that did not file that year, or left the field blank, does not match a range; older statements never stand in for current ones.
- Ordering by a measure keeps every company: those without it come last, by name.
- Employee counts the financial import flagged as unlikely, and counts of 1,000 or more with no turnover, are left out of filters and ordering as source errors. The profile still shows them as published.
- Bounds are inclusive, typed in lei or persons; thousands separators are accepted.

## Acceptance

- Filters live in the URL, combine with the existing ones, can be removed one by one, and work without JavaScript.
- Counts and facets agree with the filtered list.

## Tasks

- S12.1 Turnover and employee filters and ordering in search — [note](../tasks/s12-1-prospecting-filters.md).
