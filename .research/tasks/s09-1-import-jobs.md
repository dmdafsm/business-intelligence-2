# S09.1 Import jobs

Date: 2026-10-26. Status: completed (self-reviewed; not independently reviewed). Design: [ADR 0003](../../docs/engineering/decisions/0003-import-jobs.md). Guide: [import jobs](../../docs/engineering/import-jobs.md).

## Built

- `config/import-jobs.json`: seven jobs (onrc, mf-identification, mf-financials, seap, eu-funds, licences, anaf-status), each with a cron schedule, discovery rules for data.gov.ro and the operator commands to run. The file is validated as a whole: unknown keys, kinds without an importer, unknown placeholders, invalid patterns and cron fields are refused with every reason listed.
- `Modules/Jobs`:
  - Discovery (CKAN `package_search`, newest datasets first, applied oldest first). Matching files are pinned only when newer than the catalog per kind and series. Downloads are limited to allowed https hosts and a size limit, go to `.part` files inside the raw folder, are SHA-256 hashed, and a dataset is pinned whole or not at all.
  - `jobs.catalog_files`, merged into `SourceCatalog` by every command.
  - The run queue `jobs.runs` (one active run per job, enforced by a partial unique index), the run log `jobs.run_lines`, the cron parser, and the runner. The runner starts each command as a child process, skips commands when nothing is new and the previous run succeeded, and supports cancel and timeout. Runs left running are marked interrupted.
- Host:
  - CLI `jobs check|list|discover|run|queue|cancel|runs|log|worker`.
  - The `worker` Compose service, which holds a PostgreSQL advisory lock.
  - Operator pages `/admin/intrare`, `/admin/joburi` and `/admin/joburi/rulare/{id}`. They use token sign-in with a cookie limited to `/admin`, return 404 without a token and are no-store. They work without JavaScript; a run's page reloads by meta refresh while the run is active.
- `prepare_docker.py` creates `.artifacts/docker/admin-token`. Four embedded catalog entries gained their local path (N_CAEN, N_STARE_FIRMA, OD_CAEN_AUTORIZAT, SIRUTA), so `{latest-path:…}` works for them.
- Migration `ImportJobs`: additive (new `jobs` schema).

## Commands and results

- `python3 scripts/verify.py`: PASS. 147 backend tests (33 new: cron, job file, discovery with a fake CKAN portal, whole-dataset pinning, size and host limits, dry run, run skip/force/failure/retry, cancel, queue, schedule, child process) and 28 browser tests (4 new: operator pages without JavaScript, escaped log, phone width, 404 without a token).
- Docker, against the local database and the real data.gov.ro:
  - `jobs check`: valid.
  - `jobs discover` for every job: no new files today. That is correct, since the catalog is current. Older releases are counted and ignored. The MF identification originals that sit next to the pinned "actualizat iulie" files are reported as "left for the operator".
  - The CNAS rule was widened after this check, because some resource names have no extension.
  - A forced `licences` run through the worker succeeded in 2 s: discovery, then `licences import-all` as a child process, with every file AlreadyImported.
- Browser check: the jobs page at 360 px (tables scroll inside their wrappers) and a failed run at 1280 px, from the browser-test screenshots.

## Limits

- Revised files with an already pinned reference date are not taken automatically (see the guide).
- No rule for AFIR, ANRM or the ANCPI boundaries (unchanged since pinned).
- Missed schedule times are not made up after the worker was stopped.
- The operator page is reachable through the public tunnel too, behind the token.
- A real download through a job has not run yet: no source has a new edition today. The first ONRC snapshot after 19.10.2026 will be the first one.
