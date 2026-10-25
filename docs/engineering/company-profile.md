# Company profile

`/firma/<cui>` shows one company of the published registry snapshot; a company without a valid CUI is at `/firma?nr=<registry number>`. Search results link to it. The page reads the registry, fiscal and financial tables directly through `CompanyProfileQuery` (Profile module, read only); it has no tables of its own.

## Sections

| Section | Contents | Source shown |
| --- | --- | --- |
| Prezentare | CUI, registry number, EUID, legal form, first registration date, ONRC lifecycle and status names, registered office, parent company's country, VAT and fiscal status, other registrations with the same CUI | ONRC snapshot; MF identification edition of the latest fiscal period |
| Contact | Phone, fax and fiscal address from the latest MF period, website from ONRC | MF edition, ONRC snapshot |
| Activități | Main activity declared in the latest financial statement (Rev.2 label), authorized activities in the registration's CAEN version | ONRC snapshot |
| Istoric | Registry periods (by registry number and by CUI, so earlier numbers appear) and MF fiscal periods, newest first | ONRC yearly snapshots with their licenses; MF editions |
| Financiar | Three charts (turnover, net result, employees), a table of key indicators per year, all 21 published indicators, and the file and license of each year | Each year's MF file |

## Rules

- **Every year is listed** from 2008 (or the registration year, if later) to the latest published year. A year without a statement shows "nedepus"; a blank cell in a filed statement shows "—" with "necompletat" for screen readers; zero shows 0. In charts a grey dash marks a year without a value, a coloured baseline mark a zero.
- Profit and loss stay in separate table columns as published. The net-result chart draws the profit, or the loss below the axis; nothing else is derived.
- A value removed at import as impossible is shown missing, with a note on that year.
- Individuals (PFA, II, IF, PF, AF) have no street, phone or fax: the imports never stored them. The page says so instead of "nedeclarat".
- Charts are SVG rendered on the server: one series each, one axis, columns from a zero baseline, the latest value labeled, a native tooltip per year. Colours are the `--md-chart-*` tokens (checked for contrast and colour-vision separation in both schemes); losses are also below the axis, so colour is never the only cue. The table holds every value.
- When the registry gives the CUI to another registration with a different name, the page names it and says the link may be a source error; fiscal and financial data stay those of the CUI.
- A website link is made only from a plain host name, always over https.
- Sector "0" in the sources means no sector and is not shown.

## Measured

Warm page times on the full data: 5–9 ms (DEDEMAN SRL with 18 years, 120 activities and 5 history periods: 7 ms).
