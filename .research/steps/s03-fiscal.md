# S03 Fiscal data and contacts

Status: completed. Depends on S02. Part of the [MVP plan](../mvp-plan.md).

## Outcome

Each company has its fiscal identification history: fiscal address, phone, fax, VAT payer status, fiscal status and registered tax obligations, per yearly MF publication.

## Sources

MF "Date de identificare plătitori" on data.gov.ro, yearly 2018–2026 (August 2018, July 2019, May 2020, June 2021–2025, July 2026 update), CC BY 4.0.

## Acceptance

- All yearly files import with their publication date; each value keeps the file it came from.
- Records link to companies by valid CUI; unmatched CUIs are kept and reported.
- Blank fields stay blank; "NU" and missing are different values.

## Tasks

- S03.1 Inspect every yearly MF identification release — [note](../tasks/s03-1-fiscal-inspection.md).
- S03.2 Import all releases as taxpayer periods — [note](../tasks/s03-2-fiscal-import.md).
