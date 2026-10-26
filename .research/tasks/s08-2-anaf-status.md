# S08.2 Current ANAF status

Date: 2026-10-25. Branch: codex/representatives. Operation: [profile open data](../../docs/engineering/profile-open-data.md), [ADR 0002](../../docs/engineering/decisions/0002-anaf-web-service-harvest.md).

## What was built

- Anaf module, table `anaf.statuses`, migration `AnafStatus`; `anaf harvest [--since] [--max-requests]` and `anaf status`.
- `HarvestAnafStatus`: asks the PlatitorTvaRest v9 web service for 100 CUIs per request, 1 request per second, with backoff on errors; resumable (CUIs already asked for since the given date are skipped).
- Stored: VAT payer and periods, cash-accounting VAT, split VAT, inactive/reactivated/deregistered, RO e-Factura and its date, registration status, tax office, CAEN code, the date asked for. Not stored: address, phone, fax, bank account.
- Profile section "Situația la ANAF"; large and medium taxpayers come from the tax office (list item 7).

## Run

Approved by the maintainer (about 26,500 requests per full pass). Running in Docker as `bi2-anaf-harvest`, about 1.1 s per request; at the time of this note 2,800 requests, 280,000 CUIs found, about 7.5 hours left.

## Tests

Answers stored without contact fields; the harvest resumes; browser check of the section.

## Limits

- One pass gives the state on the day asked; it is not a history.
- The service answers only CUIs, so companies missing from the MF release are not asked.
