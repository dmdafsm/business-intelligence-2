# S14.1 CSV export

Date: 2026-10-26. Branch: codex/s14-export. Plan: [S14](../steps/s14-export.md).

## What was built

- **`CompanySearch.ExportAsync`**: returns the first 10,000 results (`MaxExport`) in the page's order, with each company's signals.
  - The request gained an optional `Limit`.
  - Search hits now carry the net result.
- **`CompanyCsv`**: writes the file.
  - Format: BOM and semicolons.
  - Escaping: quoted cells, formula-like text neutralized.
  - Missing values are left empty.
  - The last line attributes the sources and says how many rows the file holds.
- **Search page**: a "Descarcă CSV" link (handler `Csv`, same query string) next to the result count. It works without JavaScript; the bare home page redirects.

## Tests

- **Unit**: header, escaping, empty versus zero, formula neutralization, signals and the attribution line.
- **Browser (no JavaScript)**: the download keeps the filters and order, includes the 2013 values, and the bare page redirects.
- **Manual check (local data)**:

  | Export | Rows | Size | Time |
  | --- | --- | --- | --- |
  | Cluj, CAEN 4711, ordered by turnover | 3,233 | 388 KB | 0.8 s |
  | National, any turnover | 10,000 | 1.2 MB | 3.8 s |

## Limits

- 10,000 rows per file, with no paging of exports.
- No XLSX.
- The file is built in memory, which the row limit keeps bounded.
