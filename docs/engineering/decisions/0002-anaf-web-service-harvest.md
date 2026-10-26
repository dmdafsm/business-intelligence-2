# ADR 0002: Harvest the ANAF taxpayer web service from an operator command

Status: accepted by the maintainer (approved on 2026-10-25 together with the other S08 sources). Date: 2026-10-25. Related tasks: S08.4.

## Context

The repository imports from local files only, and pages never call a remote source. ANAF publishes the current VAT, cash-accounting VAT, split VAT, inactive and RO e-Factura status of every taxpayer only through a free web service (PlatitorTvaRest v9), with published limits of 100 CUIs per request and one request per second. There is no bulk file.

## Decision

- `anaf harvest` (operator command, never the web process) asks the service about every CUI of the latest MF identification release, at the published limits, and stores each answer with the date it was asked for in `anaf.statuses`. It resumes: CUIs answered since `--since` are skipped. It stops after eight consecutive failures.
- Only status fields are stored: VAT and its periods, cash-accounting VAT, split VAT, inactive/reactivated/deregistered dates, e-Factura, registration status, tax office, CAEN. Address, phone, fax and bank account are discarded.
- Pages read the latest stored answer and say which date it describes.

## Alternatives and consequences

- Calling the service when a page is viewed was rejected: pages stay local and fast, and a page view must not depend on ANAF.
- A full pass is about 26,500 requests (about 8 hours). Answers age; re-running the harvest adds a new dated answer per CUI.
- The service has no published license; the data are public tax information, attributed to ANAF on the page.

## Verification

The harvester is tested with a fake service (retry after a 503, stored fields, no contact data, resume). The first real pass ran in Docker; see the S08.4 task note.
