# Import jobs

New editions of the sources the portal already imports are found, downloaded and imported by **import jobs**. Jobs are defined in [config/import-jobs.json](../../config/import-jobs.json), not in code. Design: [ADR 0003](decisions/0003-import-jobs.md).

## Running them

With the Docker stack up (`docker compose up --build --detach --wait`), the `worker` service runs the schedule and the queue. Each job runs at most once at a time, and only one run executes at a time.

- **Page:** <http://localhost:8090/admin/joburi>. Sign in with the operator token in `.artifacts/docker/admin-token` (created by `scripts/prepare_docker.py`). The page lists jobs, schedules, last and next runs, recent runs and discovered files. **Pornește** queues a run; on a run's page you follow the log (it reloads every 10 seconds) and can cancel it. Without a token file the pages do not exist.
- **CLI** (through the `import` service, or `dotnet run --project src/BusinessIntelligence.Host --` with `ConnectionStrings__Application` set):

```sh
docker compose --profile import run --rm import jobs check
docker compose --profile import run --rm import jobs list
docker compose --profile import run --rm import jobs discover seap
docker compose --profile import run --rm import jobs queue seap --force
docker compose --profile import run --rm import jobs runs
docker compose --profile import run --rm import jobs log 12
```

`jobs discover` shows what would be downloaded now and downloads nothing. `jobs queue` hands a run to the worker. `jobs run <job>` runs a job in that process instead; it is refused while the worker holds the job lock, and in the `import` service it cannot download (the raw folder is read-only there), so in Docker use `jobs queue`. `--force` runs the commands even when nothing new was found.

## What a run does

1. **Discovery** (for jobs with `discover` rules): it asks the data.gov.ro CKAN API for the organization's datasets whose title matches the rule. Matching resources become catalog entries. A file is taken only when:
   - its resource is not pinned yet;
   - it is newer than every pinned file of its kind and series;
   - its URL is on an allowed https host, it is under the size limit, and its path stays inside the raw folder.

   The files of one dataset are downloaded and pinned together, or not at all. A rule with `wholeDataset` takes files only from a dataset none of whose files is pinned, so a release is never pinned in part. Pinned files go to `jobs.catalog_files` and join the embedded catalog for every command.
2. **Commands:** the job's operator commands run in order, each as a child process, and their output becomes the run log. If discovery found nothing new and the job's previous run succeeded, the commands are skipped. A failed command fails the run, and the next run retries. The importers publish atomically, so a failed run leaves the published data as it was.

A run left running by a stopped worker is marked failed when the worker starts again. Scheduled times missed while the worker was stopped are not made up; the next one counts from the worker's start (start a run from the page if needed).

## The job file

The worker reads the file again on every pass, so edits apply without a restart. An invalid edit is reported (`jobs check`, the page, the worker log), and the last valid file stays in use. Unknown settings are refused, so a misspelled key cannot be silently ignored.

```jsonc
{
  "timeZone": "Europe/Bucharest",            // schedules are local times
  "ckanApi": "https://data.gov.ro/api/3/action/",
  "allowedHosts": ["data.gov.ro"],           // downloads only from these hosts (and their subdomains)
  "maxFileBytes": 4294967296,
  "jobs": [{
    "id": "seap", "title": "…",
    "schedule": "0 9 * * 3",                 // minute hour day month weekday; omit for manual-only jobs
    "timeoutHours": 12,
    "discover": [{
      "organization": "agentia-pentru-agenda-digitala-a-romaniei",
      "dataset": "^Achiziții publice (?<year>\\d{4})$",   // matched against the dataset title
      "maxDatasets": 2,                      // the newest matching datasets checked
      "wholeDataset": false,
      "referenceDate": "{quarterEnd}", "year": "{year}", "family": "Q{quarter}",
      "map": { "quarter": { "I": "1", "II": "2", "III": "3", "IV": "4" } },
      "groupFromFile": "^([a-z]+)-",         // optional: separate series inside one kind, from the file name
      "files": [
        { "kind": "seap-contracts", "resource": "^Contracte T\\s*(?<quarter>IV|III|II|I)", "file": "\\.xlsx$",
          "id": "seap-contracts-{year}-q{quarter}", "path": "seap-history/{year}/{file}" }
      ]
    }],
    "commands": [["procurement", "import-all", "{raw}"]]
  }]
}
```

- Patterns are .NET regular expressions, case-insensitive. Named groups of the dataset, resource (`resource`, the CKAN resource name) and file (`file`, the URL's file name) patterns become template values. `map` translates captured values, for example Roman numerals or Romanian month names.
- Built-in values: `{file}` (URL file name), `{slug}` (it in lower case with dashes), `{modified}` (the date the resource last changed), `{datasetModified}` (the newest such date among the dataset's matched files, one date for a release), `{yearEnd}` (needs `year`), `{quarterEnd}` (needs `year` and `quarter`).
- `kind` must be a kind the embedded catalog has, because it decides the importer. A source of a new kind needs its importer first (code).
- Commands start with `registry`, `financials`, `fiscal`, `procurement`, `funds`, `licences`, `anaf` or `search`. The placeholders are `{raw}` (the raw folder), `{latest-id:<kind>}` and `{latest-path:<kind>}` (the newest pinned file of a kind).

## Jobs in the repository

| Job | Schedule | Finds | Runs |
| --- | --- | --- | --- |
| onrc | daily 07:30 | new ONRC registry snapshots (5 files) and nomenclatures | registry imports, history, search build |
| mf-identification | Mondays 08:00 | a new yearly MF identification release (`.txt` files) | `fiscal import-all` |
| mf-financials | Tuesdays 08:00 | a new year of financial statements (BL, UU, IR data and dictionaries) | `financials import-all` |
| seap | Wednesdays 09:00 | new SEAP quarters (contracts, direct purchases) | `procurement import-all` |
| eu-funds | the 1st, 09:00 | newer MIPE lists per programme | `funds import-all` |
| licences | the 2nd, 09:00 | newer ANRSC lists and CNAS provider lists | `licences import-all` |
| anaf-status | the 3rd, 01:00 | — | `anaf harvest` (about 8 hours) |

## Limits

- Revised files with the same reference date as a pinned one are not taken: for example "Situatii financiare 2024 actualizat", or "actualizat iulie" files added to an MF release that is already pinned. The run log names them ("left for the operator"). Pin them in the embedded catalog, or add a rule that takes them.
- A publisher that renames its datasets or resources makes a rule match nothing. Nothing is downloaded then; `jobs discover` shows what the rules see.
- Raw files are kept, because a history `--rebuild` needs them all. An ONRC month is about 1.6 GB.
- AFIR, ANRM and the ANCPI boundaries have no rule; their files have not changed since they were pinned.
