# S08 More public data on the profile

Status: completed. Depends on S06. Added on 2026-10-25 at the maintainer's request: go through the freely available sources one by one.

## Candidates (researched 2026-10-25)

| Source | Where | State |
| --- | --- | --- |
| Public procurement (SEAP) | data.gov.ro "Achiziții publice <year>", ADR, CC BY 4.0 (UK OGL before 2021) | 2025 (S08.1); 2007–2024 and 2026 (S08.6); no 2019 dataset |
| Current ANAF fiscal status | ANAF web service PlatitorTvaRest v9: 100 CUIs per request, 1 request per second | Harvested (S08.2), about 26,500 requests per pass |
| EU-funded projects | data.gov.ro "proiecte-contractate" (MIPE, lists per programme at 31.08.2025) and "date-privind-proiectele-pndr" (AFIR, CC BY 4.0) | Imported (S08.3) |
| APIA agricultural subsidies | No bulk list found on data.gov.ro or by search | Not available as open bulk data |
| Licences | ANRSC public utility licences (CSV, CC BY 4.0), ANRM mining licences, CNAS contracted providers | ANRSC, ANRM, CNAS imported (S08.4); other regulators publish on their own sites only |
| ANAF debtors | anaf.ro/restante: interactive search only | No bulk file |
| Large taxpayers | ANAF tax office (DGAMC and medium-taxpayer offices) from the web service | Shown with the ANAF status (S08.2) |
| Court cases | portal.just.ro search service, by party name | No bulk access; name matching unreliable |
| Map location | ANCPI administrative boundaries 2024 (shapefile, CC BY 4.0, about 33 MB) | Imported (S08.5) |

## Tasks

- S08.1 Public procurement (SEAP) 2025 — [note](../tasks/s08-1-procurement.md).
- S08.2 Current ANAF status — [note](../tasks/s08-2-anaf-status.md).
- S08.3 EU-funded projects — [note](../tasks/s08-3-eu-funds.md).
- S08.4 Licences and contracts — [note](../tasks/s08-4-licences.md).
- S08.5 Location map — [note](../tasks/s08-5-location-map.md).
- S08.6 Public procurement (SEAP) 2007–2026 — [note](../tasks/s08-6-procurement-history.md).
