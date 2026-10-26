# ADR 0004: Link representatives across companies by exact registry data

Status: accepted by the maintainer (chose this over linking firms only, 2026-10-26). Date: 2026-10-26. Related tasks: S07.2. Amends [ADR 0001](0001-representatives-personal-data.md), which said people are not linked across companies.

## Context

A profile lists who runs the company (ADR 0001). Users want to see the other companies the same representative runs. ONRC publishes no personal identifier (no CNP), only name, birth date and place of birth for natural persons, and name and address for firms acting as representatives.

Measured on the 19.10.2026 snapshot:

- **Natural persons:** 2.6M distinct name and birth date pairs, 393,000 of them at more than one registration. For 226,000 pairs the place of birth differs between rows, either homonyms or typing variants. The largest groups are insolvency practitioners, at up to 1,488 registrations.
- **Firms acting as representatives:** 22,700, of which 10,400 are at more than one registration.

## Decision

- **Matching key**, stored as `registry.representatives.person_key` and set at import:
  - Natural persons: folded name, full birth date and folded place of birth. Diacritics are removed, letters capitalized, and everything except letters and digits dropped.
  - Firms acting as representatives: folded name.
  - Rows without a birth date or place of birth (natural) or without a name get no key and are never linked.
- **Profile:** for each representative, the other registrations with the same key in the same snapshot are shown, with a count and the first 20 linked. A visible note says the link rests on identical name, birth date and place of birth in the registry and may join different people or miss variants of the same one.
- **Still not built:**
  - person pages or search by person;
  - showing the full birth date or the place of birth;
  - ranking or scoring people;
  - links across snapshots.

## Alternatives and consequences

- **Firms only:** this would have needed no change to ADR 0001. The maintainer chose to include natural persons.
- **Name and birth date without the place of birth:** more matches, but it joins more homonyms. The place of birth is added so that a link is wrong less often. The cost is missing links when the source spells the place differently.
- **Privacy:** the page now gathers a natural person's directorships in one place. The data is public in the ONRC dataset (CC BY 4.0), but bringing it together is profiling. Keep the warning, no person pages and no search by person. Treat any request for correction or erasure as a reason to revisit this ADR.
- **Reversible:** remove the profile section and the column.

## Verification

- An import test checks that the key folds diacritics and punctuation and is null without a birth place.
- A profile test checks that links stay within the snapshot and leave out the company itself.
- A browser test checks the note and the links.
- See the S07.2 task note for the real-data run.
