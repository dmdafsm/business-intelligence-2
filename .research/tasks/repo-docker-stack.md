# REPO Docker stack owns its data

Date: 2026-10-25. Branch: codex/docker. Operation: [Docker guide](../../docs/engineering/docker-local.md).

## What changed

- Compose names everything for this repository: project `business-intelligence-2`, volume `business-intelligence-2-postgres-data`, network `business-intelligence-2`, host port 8090 (the older `business-intelligence` stack keeps 8080 and its own volume; both run side by side).
- PostgreSQL gets 3 GB, 4 CPUs, 1 GB shared memory and larger buffers, WAL and maintenance memory for the national imports.
- New `import` service (profile `import`) with `.research/raw` mounted read-only at `/data/raw`, and `scripts/docker_import_all.py`, which runs every import in order and rebuilds the search tables.

## Full load in Docker

From an empty volume on the maintainer Mac (Docker Desktop, 8 GB VM shared with the older stack):

| Step | Time |
| --- | ---: |
| Nomenclatures, SIRUTA | 7 s |
| OD_FIRME, statuses, activities | 157 + 243 + 163 s |
| Registry history, 14 snapshots | 1,615 s |
| Financial statements 2008–2025 | 85 s |
| Fiscal identification 2018–2026 | 1,233 s |
| Search tables | 203 s |

About 62 minutes in total; the database takes 19 GB. Result: 4,126,414 companies, 6,633,827 registration periods, 13,510,119 statements, 7,970,235 taxpayer periods. The last two period counts differ slightly from the earlier local run (6,633,773 and 7,970,233); the cause was not investigated. Search, grouping and profiles answer on http://localhost:8090 (warm times not measured separately in Docker).

## Commands run

`python3 scripts/prepare_docker.py`, `docker compose up --build --detach --wait`, `python3 scripts/docker_import_all.py` (exit 0), page checks with curl on port 8090, `python3 scripts/verify.py` (passed: 96 backend, 24 browser tests).

## Limits

- The Docker verification image (`--profile verify`) was not rerun for this change.
- History and fiscal imports are 2–2.5 times slower in Docker than against a native PostgreSQL on the same machine.
