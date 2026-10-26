# More open data on the profile (S08)

Besides the registry, fiscal and financial data, the company profile shows public procurement ([procurement import](procurement-import.md)) and the sources below. All are pinned in the [source catalog](../../src/BusinessIntelligence.Modules/Registry/Sources/catalog.json) by size and SHA-256, except the ANAF web service, and each section on the page names its source, date and license.

## Commands

```sh
dotnet …/BusinessIntelligence.Host.dll funds import-all .research/raw
dotnet …/BusinessIntelligence.Host.dll licences import-all .research/raw
dotnet …/BusinessIntelligence.Host.dll registry import boundaries-ancpi-unitate-administrativa-judet .research/raw/boundaries/ancpi-2024/unitate_administrativa_judet.zip
dotnet …/BusinessIntelligence.Host.dll registry import boundaries-ancpi-unitate-administrativa-uat .research/raw/boundaries/ancpi-2024/unitate_administrativa_uat.zip
dotnet …/BusinessIntelligence.Host.dll anaf harvest [--since yyyy-MM-dd] [--max-requests N]
dotnet …/BusinessIntelligence.Host.dll anaf status
```

In Docker, `scripts/docker_import_all.py` runs the file imports; the ANAF harvest is started separately because it takes about 8 hours.

## EU-funded projects (`funds.projects`)

- **Sources:** the Ministry of Investments and European Projects' lists of contracted projects per operational programme (POR, POIM, POCU, POC, POAT at 31.08.2025; POCA, POAD at 31.12.2024; UK OGL), and AFIR's rural development projects (PNDR 2007–2013 and 2014–2020, CC BY 4.0).
- **Reading:** the Ministry workbooks have a title block and a header row that differs by programme; columns are found by their Romanian or English labels. AFIR files are CSV with fixed columns. A newer list for the same programme replaces the older one.
- **Linking:** only AFIR 2007–2013 gives the beneficiary's tax code. Other projects are linked on the profile when the beneficiary name equals the company's current name (folded) within the same county and no other company in that county has that name; the page says "după denumire și județ". Partnerships (POCU) list several names in one cell and are not linked. A beneficiary identified by a personal code is stored without name or code; AFIR addresses are not stored.
- **Result:** 179,199 projects; 79,874 linked by CUI, about 65,000 by name and county.

## Licences and contracts (`licences.licences`)

- **ANRSC** public utility licences (valid, withdrawn, rejected; CSV, CC BY 4.0): the operator code is the county's letters plus the CUI. 1,618 rows, 1,124 linked.
- **ANRM** mining licences (exploitation, exploration, energy resources; shapefile attribute tables, CC BY 4.0): only the holder's name. Linked when the name equals exactly one company's current name. 957 rows, 255 linked.
- **CNAS** contracts with health care providers and pharmacies at 31.03.2026 (old .xls, CC BY 4.0): by tax code. 26,956 rows, 14,204 linked (most others are individual medical practices, not in the trade registry).

## Current ANAF status (`anaf.statuses`)

From the ANAF web service, see [ADR 0002](decisions/0002-anaf-web-service-harvest.md): VAT payer and periods, cash-accounting VAT, split VAT, inactive/reactivated/deregistered, RO e-Factura and its date, registration status, tax office and CAEN code, for each CUI of the latest MF release, with the date asked for. The page marks a **large taxpayer** when the tax office is the Directorate for Large Taxpayers (DGAMC) and a medium one for the medium-taxpayer offices. Address, phone, fax and bank account are not kept.

## Location map (`registry.area_outlines`)

ANCPI administrative boundaries (07.03.2024, CC BY 4.0) for counties and UATs, keyed by SIRUTA code, are simplified (Douglas–Peucker, 1,500 m for counties, 350 m for UATs) into SVG paths in one national frame (Stereo70 metres, north up). The profile draws Romania with the company's county highlighted and a county close-up with its UAT. County outlines total about 32 KB, all UATs about 940 KB.

## Not available

- APIA agricultural subsidy beneficiaries: no bulk list found (only payment press releases).
- ANAF list of debtors with overdue taxes: an interactive search page only.
- Court cases: the court portal searches one party name at a time, and names do not identify companies reliably.
