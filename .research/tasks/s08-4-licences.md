# S08.4 Licences and contracts with authorities

Date: 2026-10-25. Branch: codex/representatives. Operation: [profile open data](../../docs/engineering/profile-open-data.md).

## What was built

- Licences module, table `licences.licences`, migration `Licences`; `licences import-all`.
- ANRSC utility licences (CSV), ANRM mining licences (DBF attribute tables in zip), CNAS health providers (old .xls through ExcelDataReader 3.9.0, MIT, added to the third-party notices).
- Platform readers: `DelimitedReader`, `Dbf`, `LegacyXls` and a `Spreadsheet` dispatcher.
- Profile section "Licențe și contracte cu autorități".

## Result

ANRSC 1,618 rows (1,124 linked), ANRM 957 (255 linked by unique name), CNAS 26,956 (14,204 linked).

## Tests

CSV, shapefile and workbook lists; linking; browser check of the section.

## Limits

ANRM gives only the holder's name. Most unlinked CNAS providers are individual practices outside the trade registry. Other regulators publish only on their own sites.
