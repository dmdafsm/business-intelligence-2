# S14 CSV export of search results

Status: completed. Depends on S05, S12 and S13. Added on 2026-10-26 at the maintainer's request, carried over from the original repository (F08), limited to the search list. Accounts, saved lists and XLSX stay out of scope.

## Outcome

Any search or filtered list can be downloaded as a CSV file for a spreadsheet: the same filters and order as the page, at most 10,000 rows, with identification, place, state, the latest year's turnover, employees and net result, and the warning signals.

## Rules

- Semicolon-separated UTF-8 with a byte order mark, so Romanian Excel opens it directly; numbers are plain, a missing value is an empty cell, never 0.
- A cell a spreadsheet would run as a formula is prefixed with an apostrophe.
- The last line names the sources and their licenses (ONRC and MF under CC BY 4.0 or OGL by year, ANAF) and says how many of the results the file holds.
- The bare home page has no list and no export.

## Tasks

- S14.1 CSV export from the search page — [note](../tasks/s14-1-csv-export.md).
