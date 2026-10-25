# Registry import

Operator commands that load the ONRC registry, its nomenclatures and SIRUTA into PostgreSQL. Pages never call a source; they read what these commands publish.

## Files

Every importable file is pinned in [catalog.json](../../src/BusinessIntelligence.Modules/Registry/Sources/catalog.json) with its URL, publisher, license, reference date, size and SHA-256. An import accepts a local file only when its size and hash match the entry, and records the entry in `registry.source_files` for attribution. Download files into the ignored `.research/raw/` folder; never commit them.

To add a newer snapshot, add a catalog entry with the new file's URL, size and hash, then import it.

## Commands

Run them in this order against an explicitly configured database (`ConnectionStrings__Application`):

```sh
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll registry import onrc-n-caen-2026-10-19 .research/raw/onrc-2026-10-19/N_CAEN.CSV
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll registry import onrc-n-stare-firma-2026-10-19 .research/raw/onrc-2026-10-19/N_STARE_FIRMA.CSV
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll registry import ins-siruta-s1-2024 .research/raw/siruta-s1-2024/siruta_s1_2024.xlsx
dotnet src/BusinessIntelligence.Host/bin/Release/net10.0/BusinessIntelligence.Host.dll registry import onrc-firme-2026-10-19 .research/raw/onrc-2026-10-19/OD_FIRME.CSV
```

`registry status` lists the published files and row counts; `registry show <cui|registry-number>` prints one company with its registrations.

Each command prints a JSON report. Rerunning an already imported file is a no-op (`AlreadyImported`). A wrong hash, a malformed file or a failure rolls back everything; the previously published data stays as it was.

## What the OD_FIRME import does

All in one transaction:

1. Streams the file into a temporary table (binary COPY), dropping personal numeric codes (CNP) found in the CUI column before they reach the database.
2. Checks the SHA-256 of what was read.
3. Resolves each distinct county/locality text to SIRUTA codes (county, UAT, locality) with the matcher ported from the original repository.
4. Merges rows that share a registry number: one valid CUI wins over zero or missing ones; other fields take their most frequent value; disagreeing fields are listed in `conflicting_fields`; two different valid CUIs leave the CUI empty with state `Conflict`.
5. Groups registrations into companies by valid CUI (a company re-registers when it moves county or gets a 2024-format number). The latest registration is the current one. Without a valid CUI, a registration is its own company.
6. Publishes the snapshot and deletes the previous one.

Sole traders and other individual forms (PFA, II, IF, PF, AF) keep only county, locality, sector and postal code; the street address is not stored.

On the 19.10.2026 snapshot (Apple M-series laptop, local PostgreSQL 18): 65 s, about 300 MB peak memory for the command; 4,224,673 registrations, 4,126,414 companies, 99.87% resolved to at least a UAT. The tables take about 2.3 GB with their indexes.

## Not covered yet

Company status and authorized CAEN activities come from `OD_STARE_FIRMA.CSV` and `OD_CAEN_AUTORIZAT.CSV`, which are not downloaded yet. Older snapshots (history) are a later task. In Docker, the raw files must be mounted into the container for these commands; that setup is not done yet.
