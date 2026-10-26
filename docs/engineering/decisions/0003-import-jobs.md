# ADR 0003: Import jobs with discovery, a worker and an operator page

Status: accepted by the maintainer (requested on 2026-10-26: admin page and CLI, automatic discovery and download, internal scheduler). Date: 2026-10-26. Related tasks: S09.1.

## Context

Every source file is pinned in the embedded catalog (`Registry/Sources/catalog.json`) and imported by an operator command. A new edition (a monthly ONRC snapshot, a SEAP quarter, a new MF year) therefore needs a catalog edit, a build and a manual run. The importers already pick their files from the catalog by kind and skip what is imported, so new editions need no new parsing code. They need a way to be found, downloaded, pinned and run.

## Decision

- **Job definitions are configuration.** `config/import-jobs.json` (mounted read-only at `/config`) lists jobs. Each job has a cron schedule, optional discovery rules and the operator commands to run. Adding or changing a job edits this file, not the code.
- **Discovery** asks the data.gov.ro CKAN API (`package_search`) for datasets of one organization whose title matches a pattern. Each matching resource maps to a known catalog kind, with id, local path, reference date, year and family built from named captures. A file is accepted only when it is newer than the newest pinned file of the same kind and group. The download runs only from an allowed host, under a size limit, to a path inside the raw folder. The file is hashed and pinned in `jobs.catalog_files` (URL, publisher, license, size, SHA-256, CKAN resource id and dates).
- **The catalog is the embedded files plus the discovered ones.** Every operator command loads the discovered entries at start. The importers stay unchanged and keep verifying size and hash.
- **Runs** are rows in `jobs.runs` with their output in `jobs.run_lines`. A run is queued by the schedule, the page or the CLI. Each command runs as a child process of the same binary, so its memory is released after the step and its JSON output becomes the run log. One run executes at a time, under a PostgreSQL advisory lock. A run whose discovery found nothing new skips its commands, unless the job's previous run failed or the run is forced.
- **Worker:** a `worker` service in Docker (`jobs worker`, same image) runs the schedule and the queue. The web process never calls a remote source and never runs an import; it only queues and shows runs.
- **The operator page** `/admin/joburi` lists jobs, their next and last runs, and recent discovered files. It can queue or cancel a run and show a run's log. It needs an operator token (`Admin:TokenFile`, a Docker secret), entered once on a sign-in form and kept in an HTTP-only cookie. Without a configured token the page does not exist (404).

## Alternatives and consequences

- Hangfire or Quartz were rejected: they add a package and their own schema, for one queue with one worker. The cron parser covers the five standard fields.
- Running imports inside the worker process was rejected: the SEAP import peaks at 2.5 GB and every command already exists as a tested CLI entry point.
- A loopback-only page was rejected: the public tunnel reaches the web container from localhost too.
- Revised files with the same reference date as a pinned one (for example "2024_actualizat") are not taken automatically; they need a catalog entry or a rule of their own.
- Downloaded raw files are kept: a `--rebuild` of a history needs them all. Disk use grows by about 1.6 GB per ONRC month.

## Verification

Rule matching, templates and cron are unit-tested. Discovery, download, pinning, the queue, the stale-run recovery and the step runner are tested against real PostgreSQL with a fake CKAN server. The page is browser-tested. The first real run is recorded in the S09.1 task note.
