# Architecture

## Shape

- One ASP.NET Core Razor Pages application (`BusinessIntelligence.Host`) with two roles: the web portal and operator commands (`database migrate` and the import and build commands of each module). Imports run as commands. One background process exists: the import-jobs worker (`jobs worker`, [ADR 0003](engineering/decisions/0003-import-jobs.md)), which discovers new source files on schedule and runs those same commands as child processes.
- One class library (`BusinessIntelligence.Modules`) holding modules as namespaces: `Platform` (persistence, hosting), `Registry`, `Fiscal`, `Financials`, `Search`, `Profile` and the others. A module writes only its own tables; `Profile` only reads the others to assemble a company page. `Market` reads the registry and writes the `market` statistics tables. `Search` reads the registry, the latest financial year and `risk.signals`. `Risk` reads the registry, ANAF, financials and benchmarks and writes `risk.signals`.
- PostgreSQL holds everything: staging tables for imports, published tables for reads, and history.

## Data flow

1. A raw file is downloaded once into the ignored raw-data folder (`.research/raw/` by default) and recorded in the source catalog (URL, license, hash): the embedded [catalog.json](../src/BusinessIntelligence.Modules/Registry/Sources/catalog.json) for the first load, `jobs.catalog_files` for files the import jobs found later.
2. An import command streams it into staging with COPY in bounded batches, validates it, and publishes atomically. Rerunning the same file is a no-op.
3. Pages read published tables and projections only. They never call a remote source.

Companies are keyed by valid CUI, with the registry number as fallback. Sources join on that key directly; ambiguous or invalid links are flagged, not hidden.

## Conventions

- Vertical slices: each operation keeps its input, validation and handler together.
- EF Core for schema and ordinary writes; parameterized SQL for bulk and analytical reads.
- Real PostgreSQL in tests, real Chromium in browser tests.
- Decimal for money, explicit reference periods, blank and zero kept distinct.
- No accounts, roles or per-use source permission gates; the operator page is protected by a token. Any of these can change with its own decision record.
