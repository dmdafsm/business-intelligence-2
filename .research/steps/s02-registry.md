# S02 Registry

Status: in progress. Depends on S01. Part of the [MVP plan](../mvp-plan.md).

## Outcome

Every company in the ONRC open-data registry exists in the database with its identity, legal form, registered office, website, status and authorized activities, resolved to SIRUTA geography, with history from older snapshots.

## Sources

- ONRC "Firme înregistrate" datasets on data.gov.ro: `OD_FIRME`, `OD_STARE_FIRMA`, `OD_CAEN_AUTORIZAT`, `OD_SUCURSALE_ALTE_STATE_MEMBRE`; latest snapshot 19.10.2026, older snapshots back to 2013.
- ONRC nomenclatures (`N_CAEN`, `N_STARE_FIRMA`, `N_VERSIUNE_CAEN`) and the CAEN Rev.2/Rev.3 correspondence.
- INS SIRUTA (latest published edition).
- Raw files already downloaded for the old repo (`OD_FIRME.CSV` 19.10.2026, nomenclatures, SIRUTA S1 2024) are reused after hash checks.

## Acceptance

- A simple source catalog records URL, publisher, license, attribution and download date per file.
- The import streams in bounded memory, is idempotent and publishes a snapshot atomically; rerunning the same file changes nothing.
- Company key: valid CUI when present; registry number otherwise. Invalid or duplicate CUIs are flagged, not dropped.
- Older snapshots add history (name, address, legal form, status changes) without overwriting the current values.

## Tasks

- S02.1 Inspect registry, nomenclature and SIRUTA formats — [note](../tasks/s02-1-registry-inspection.md).
- S02.2 Import nomenclatures, SIRUTA and the latest OD_FIRME snapshot — [note](../tasks/s02-2-registry-import.md).
- S02.3 Import company statuses (`OD_STARE_FIRMA`) and authorized CAEN activities (`OD_CAEN_AUTORIZAT`) — [note](../tasks/s02-3-statuses-and-activities.md).
- S02.4 Import older snapshots as history — [inspection](../tasks/s02-4-history-inspection.md), [note](../tasks/s02-4-registry-history.md).
