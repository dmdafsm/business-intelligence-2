# S06.1 Company profile

Date: 2026-10-25. Branch: codex/registry. Rules and sections: [company profile guide](../../docs/engineering/company-profile.md).

## What was built

- `CompanyProfileQuery` (Profile module, read only): the company of the published snapshot by CUI, or by registry number when it has no valid CUI; its registrations, statuses, authorized activities, registry periods (by registry number and by CUI), MF fiscal periods, and every financial year in its range with each year's source file. Published financial years come from the dataset pointers, not from scanning statements.
- `/firma/<cui>` and `/firma?nr=…`: sections Prezentare, Contact, Activități, Istoric, Financiar, each with its source note (publisher, dataset, period, license); 404 for an unknown company. Search results link to it.
- Server-rendered SVG column charts (turnover, net result, employees) with validated chart colour tokens for light and dark, native tooltips and a gap mark for missing years; the key-indicator table, all 21 indicators and the per-year sources below them.
- Search: earlier registry numbers from the history now find the company (4,862,067 numbers instead of 4,224,673).
- New components on `/Design`: facts list, source note, chart.

## Real-data check

Profiles of DEDEMAN SRL (18 filed years, 120 activities, renumbered in 2025), ROUTIER EUROPEAN TRANSPORT SA (renamed, moved, and a second registry number with another company's name under the same CUI), COSTA VICTORY SRL (losses, then nothing filed after 2018), a PFA and a PF without CUI: 5–9 ms warm. Found and fixed: sector "0" shown as a sector; zero drawn like a missing year in charts; the result link for companies without CUI was empty. `search build` with the earlier numbers: 263 s, 300 MB peak.

## Tests

- PostgreSQL: profile by CUI (registrations, activities with names, history by number and by CUI, latest fiscal period, every year 2010–2014 with missing years, blank and zero kept apart, source file per year); by registry number only for companies without CUI; unknown identifiers; an earlier registry number found by search.
- Chart: Romanian compact values, losses below the baseline, gaps and zeros without a column.
- Browser (Chromium, no JavaScript): result to profile, every section with a source note, contacts and history; financial table with "nedepus", "necompletat" and 0, one negative column, per-year sources; individual without CUI (no contacts, no financials), 404; phone width without horizontal scroll and section links.

## Limits

- Years after a company was struck off still show "nedepus" up to the latest published year; the registry gives no strike-off date.
- Registry periods are bounded by snapshot dates; a change between two snapshots appears at the later one.
- When the registry gives one CUI to registrations with different names (ROUTIER above), the page flags it and names the other registration; nothing decides which is right. Not covered by an automated test.
- Tax obligation codes other than 100, 120 and 602 are shown as codes.
- Not run in Docker at the time; the full import later ran in Docker ([REPO Docker stack](repo-docker-stack.md)).
