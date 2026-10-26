# REPO English names in code

Date: 2026-10-26.

## What changed

- Razor pages renamed with their routes kept: `Firma` → `Company` (`/firma/{cui}`), `Eroare/Index` → `Errors/Index` (`/Eroare/{code}`; `Error` is refused by CA1716), `Admin/Intrare` → `Admin/SignIn` (`/admin/intrare`), `Admin/Joburi` → `Admin/Jobs` (`/admin/joburi`), `Admin/Rulare` → `Admin/Run` (`/admin/joburi/rulare/{id}`).
- Code identifiers: `ImportOnrcFirme` → `ImportOnrcCompanies`, `OnrcFirmeReader` → `OnrcCompanyReader`, `FirmeRow` → `CompanyRow`, `FirmeReadCounts` → `CompanyReadCounts`, `StripSocietate` → `StripCompanyPrefix`, plus the test helpers and test names that used them.
- The language rule is in the [coding standards](../../docs/engineering/coding-standards.md): code in English, page text and URLs in Romanian.

## Verification

- `python3 scripts/verify.py`: PASS (147 backend, 28 browser tests, which open the profile, error and operator pages through their unchanged URLs).

## Limits

- Strings and constants that name source files or columns (`OD_FIRME`, `DENUMIRE`) stay as published. Migration and database names were not touched.
