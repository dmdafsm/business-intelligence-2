# ADR 0001: Representatives: keep every published field, show name, role and birth year

Status: accepted by the maintainer; the rule against linking people across companies is replaced by [ADR 0004](0004-linking-representatives.md). Date: 2026-10-25. Related tasks: S07.1.

## Context

ONRC publishes, in the same CC BY 4.0 dataset as `OD_FIRME`, two files of the people and firms that run registered companies: `OD_REPREZENTANTI_LEGALI` (administrators, legal representatives, liquidators, about 3.7M rows) and `OD_REPREZENTANTI_IF` (members and representatives of family enterprises). Natural persons appear with their name, birth date and place of birth; firms acting as representatives (mostly liquidators) with a locality, county and country. There is no CNP. The [security rules](../security-and-data.md) kept representatives off the pages until the maintainer decided how to show them. Shareholders and beneficial owners are not open data (ONRC gives them only through paid access with a legitimate interest) and are out of scope.

## Decision

The maintainer decided (2026-10-25): **store every published column internally; show natural persons with name, role and birth year only.**

- `registry.representatives` keeps all columns of both files, per snapshot, with a `person_kind`: natural (a birth date is given), legal (no birth date, an address is given) or unknown.
- Pages read representatives only through `CompanyProfileQuery`, which selects name, role, birth year and, for legal persons only, locality, county and country. Full birth date, place of birth and anything else stay in the database.
- People are not linked across companies (no person pages, no search by person): name plus birth data does not identify a person, and homonyms exist. Changed on 2026-10-26 by [ADR 0004](0004-linking-representatives.md): the profile links representatives with identical name, birth date and place of birth, with a warning; there are still no person pages or search by person.
- A new ONRC snapshot replaces the previous snapshot's representatives, as with the other annexes.

## Alternatives and consequences

- Storing only the shown fields (minimisation) was the default recommendation; the maintainer chose to keep the full rows for later uses. The database therefore holds personal data (birth date and place) of about three million people: database dumps and backups must be treated as personal data, and access to the database stays operator-only.
- There is no procedure yet for correction or erasure requests; the source is the authority, and a new snapshot replaces the rows.
- Reversible: dropping the extra columns later is a migration.

## Verification

Import tests check that every column is stored and that the profile query and page expose only name, role, birth year and, for legal persons, their place. See the S07.1 task note for the real-data run.
