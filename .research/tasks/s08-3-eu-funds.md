# S08.3 EU-funded projects

Date: 2026-10-25. Branch: codex/representatives. Operation: [profile open data](../../docs/engineering/profile-open-data.md).

## What was built

- Funds module, table `funds.projects`, migration `Funds`; `funds import-all`.
- Ministry (MIPE) lists of contracted projects per programme (xlsx, columns found by Romanian or English labels) and AFIR PNDR projects (CSV).
- Linking by CUI (AFIR 2007–2013), otherwise by exact folded name within the county when unique; the page marks name links. Personal-code beneficiaries are stored without name or code; AFIR addresses are not stored.
- Profile section "Fonduri europene".

## Result

179,199 projects; 79,874 linked by CUI, about 65,000 by name and county.

## Tests

Ministry workbook with title block and sub-header, AFIR CSV, personal-code beneficiary withheld, links by CUI and by name in the county, a name in another county left unlinked; browser check of the section.

## Limits

POCU partnerships list several names in one cell and are not linked. Lists are at 31.08.2025 (POCA, POAD at 31.12.2024).
