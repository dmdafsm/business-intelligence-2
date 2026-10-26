# S09 Import jobs

Status: completed. Depends on S08. Added on 2026-10-26 at the maintainer's request: import future data without code changes. Design: [ADR 0003](../../docs/engineering/decisions/0003-import-jobs.md).

## Outcome

An operator keeps the portal current without editing code. New editions of the sources already imported (ONRC monthly snapshots, MF identification releases, MF financial years, SEAP quarters, EU-funds and licence lists) are found on data.gov.ro, downloaded, pinned and imported on a schedule. The ANAF status harvest runs on a schedule too. Runs and their logs can be seen and started from an operator page and from the CLI.

## Acceptance

- Jobs are defined in `config/import-jobs.json`: schedule, discovery rules, commands. An invalid file is refused with the reason (`jobs check`).
- Discovery pins only files newer than the catalog, from allowed hosts, within a size limit, under the raw folder. A rerun downloads nothing twice.
- One run at a time. A run interrupted by a restart is marked failed. A failed run leaves the published data as it was (the importers publish atomically) and the next run retries.
- `/admin/joburi` requires the operator token, works without JavaScript and on mobile, and shows job, run and log.

## Tasks

- S09.1 Job manager: catalog, discovery, worker, CLI and operator page — [note](../tasks/s09-1-import-jobs.md).
