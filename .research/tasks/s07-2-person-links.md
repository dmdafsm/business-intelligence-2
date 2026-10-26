# S07.2 Representatives linked across companies

Date: 2026-10-26. Branch: codex/s07-person-links. Decision: [ADR 0004](../../docs/engineering/decisions/0004-linking-representatives.md), chosen by the maintainer over linking firms only.

## What was built

- `registry.representatives.person_key`, migration `RepresentativeKeys`, with a backfill and an index on (snapshot, key).
  - The key is set at import by `RepresentativeKeys.Sql`.
  - For natural persons it combines the folded name, the birth date and the folded place of birth.
  - For firms acting as representatives it is the folded name.
  - Rows without the needed data get no key.
- On the local data, 3,312,436 natural-person rows and 310,822 firm rows got a key. 185,943 rows got none.
- Profile:
  - "Persoane" has an "Alte firme" column. A `details` element lists the first 20 other registrations, active ones first, with their state and the role held, and gives the total.
  - A note under the table says the link may join different people or miss spelling variants.
- The key holds the full birth date and place. It stays inside `CompanyProfileQuery` and never reaches the page.
- Data rules and ADR 0001 now point to ADR 0004.

## Tests

- **Import**:
  - Keys fold diacritics and punctuation.
  - Firms are keyed by name.
  - An unreadable birth date and an unknown kind get no key.
- **Profile**: links ignore spelling differences but not a different place of birth, and exclude the company itself.
- **Browser (no JavaScript)**:
  - The "1 firmă" disclosure leads to the linked profile.
  - The warning is shown.
  - Neither the full birth date nor the place appears.
  - The phone test still has no horizontal scroll with all disclosures open.
- **Manual checks on the real data**:

  | Profile | Links | Load time |
  | --- | --- | --- |
  | DEDEMAN | 2 other firms for one administrator | 0.5 s |
  | Practitioner (CUI 1546877) | 1,737 linked registrations, 20 shown | 0.7 s |

## Limits

- Links can be wrong: homonyms with the same birth date and place are joined.
- Links can be missing: the same person with a differently written place of birth is not linked.
- Only the current snapshot is linked.
- There are no person pages and no search by person (by decision).
