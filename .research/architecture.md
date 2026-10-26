# Architecture

Date: 2026-10-25. Carried over from the original repository in simplified form; see the [MVP plan](mvp-plan.md) for scope.

## Shape

- One ASP.NET Core Razor Pages application (`BusinessIntelligence.Host`) with two roles: the web portal and operator commands (`database migrate`, and import commands added per step). Imports run as commands. Since S09 one background process exists: the import-jobs worker (`jobs worker`, [ADR 0003](../docs/engineering/decisions/0003-import-jobs.md)), which discovers new source files on schedule and runs those same commands as child processes.
- One class library (`BusinessIntelligence.Modules`) holding modules as namespaces: `Platform` (persistence, hosting), and per step `Registry`, `Fiscal`, `Financials`, `Search`, `Profile`. A module writes only its own tables; `Profile` only reads the others to assemble a company page. `Market` (S11) reads the registry and writes the `market` statistics tables. `Search` reads the registry, the latest financial year (S12) and `risk.signals` (S13). `Risk` (S13) reads the registry, ANAF, financials and benchmarks and writes `risk.signals`.
- PostgreSQL holds everything: staging tables for imports, published tables for reads, and history.

## Data flow

1. A raw file is downloaded once into ignored `.research/raw/` and recorded in the source catalog (URL, license, hash): the embedded catalog for the first load, `jobs.catalog_files` for files the import jobs found later.
2. An import command streams it into staging with COPY in bounded batches, validates it, and publishes atomically. Rerunning the same file is a no-op.
3. Pages read published tables and projections only. They never call a remote source.

Companies are keyed by valid CUI, with the registry number as fallback. Sources join on that key directly; ambiguous or invalid links are flagged, not hidden.

## Conventions kept from the original repository

- Vertical slices: each operation keeps its input, validation and handler together.
- EF Core for schema and ordinary writes; parameterized SQL for bulk and analytical reads.
- Real PostgreSQL in tests, real Chromium in browser tests.
- Decimal for money, explicit reference periods, blank and zero kept distinct.

## Dropped from the original repository

Per-use source permission gates, manual identity confirmation, accounts and roles, portfolios, exports, Hangfire workers and OpenTelemetry. Any of them can return with its own plan.
