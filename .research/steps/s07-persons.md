# S07 Persons of interest

Status: in progress. Depends on S02, S06. Added after the MVP on 2026-10-25 at the maintainer's request.

## Outcome

A company profile shows who runs the company (administrators, legal representatives, liquidators, family-enterprise members), and, where a lawful source exists, who owns it.

## Sources

- ONRC `OD_REPREZENTANTI_LEGALI` and `OD_REPREZENTANTI_IF`, same CC BY 4.0 dataset and snapshot as `OD_FIRME`: names and roles of the people and firms that run registrations.
- Shareholders (asociați/acționari) and beneficial owners (RBR) are not open data: ONRC provides them only through paid access with a legitimate interest. Not available without that decision.

## Acceptance

- Every published column is stored; pages show natural persons with name, role and birth year only ([ADR 0001](../../docs/engineering/decisions/0001-representatives-personal-data.md)).
- The profile says which roles are shown and that shareholders are not open data.

## Tasks

- S07.1 Import ONRC representatives and show them on the profile — [note](../tasks/s07-1-representatives.md).

Open: shareholders and beneficial owners (needs a paid-access decision); history of representatives (only the current snapshot is published).
