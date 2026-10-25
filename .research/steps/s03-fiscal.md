# S03 Fiscal data and contacts

Status: planned. Depends on S02. Part of the [MVP plan](../mvp-plan.md).

## Outcome

Each company has its fiscal identification history: fiscal address, phone, fax, VAT payer status, fiscal status and registered tax obligations, per yearly MF publication.

## Sources

MF "Date de identificare plătitori" on data.gov.ro, yearly 2018–2026 (August 2018, July 2019, May 2020, June 2021–2026). The June 2026 dataset files (A/B, updated in July) are already downloaded in the old repo.

## Acceptance

- All yearly files import with their publication date; each value keeps the file it came from.
- Records link to companies by valid CUI; unmatched CUIs are kept and reported.
- Blank fields stay blank; "NU" and missing are different values.

## Tasks

To be split during readiness.
