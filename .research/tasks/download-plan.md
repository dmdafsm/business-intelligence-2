# Download plan for S04 and S02.4

Date: 2026-10-25. Built from the data.gov.ro CKAN API (`package_show` / `package_search`); sizes are from CKAN metadata or HTTP HEAD, `?` where the server reports none. Nothing below is downloaded until the maintainer approves.

## S04 — MF financial statements, 2008–2025

Company form families only: `BL_BS_SL` (long, short and simplified balance sheets; `web_2008.txt` in 2008), `UU` and `IR`. Banks, insurers, brokers, pension funds, NGOs and investment funds are left out. Each file comes from the latest dataset that publishes that year (the 2020 and 2021 datasets republish revised 2011–2020 files; 2023 and 2024 have updated files). Full list with URLs: [mf-financials-download-manifest.json](../mf-financials-download-manifest.json).

| Year | Family | Data file | Size | Dictionary | Dataset |
| --- | --- | --- | --- | --- | --- |
| 2008 | BL | web_2008.txt | ? | WEB_BL_BS_SL_AN2008.csv | 2008 |
| 2009 | BL | web_BL_BS_SL_an2009.txt | ? | WEB_BL_BS_SL_AN2009.csv | 2009 |
| 2010 | BL | web_BL_BS_SL_an2010.txt | ? | WEB_BL_BS_SL_AN2010.csv | 2010 |
| 2011 | BL | WEB_BL_BS_SL_AN2011.txt | 59.7 MB | WEB_BL_BS_SL_AN2011.csv | 2020 |
| 2011 | UU | WEB_UU_2011.txt | 1.0 MB | web_UU_2011.csv | 2020 |
| 2012 | BL | WEB_BL_BS_SL_AN2012.txt | 60.4 MB | WEB_BL_BS_SL_AN2012.csv | 2021 |
| 2012 | IR | WEB_IR_AN2012.txt | 0.0 MB | WEB_IR_2012.csv | 2021 |
| 2012 | UU | WEB_UU_2012.txt | 1.0 MB | web_UU_2012.csv | 2021 |
| 2013 | BL | WEB_BL_BS_SL_AN2013.txt | 61.0 MB | WEB_BL_BS_SL_AN2013.csv | 2021 |
| 2013 | IR | WEB_IR_AN2013.txt | 0.0 MB | WEB_IR_2013.csv | 2021 |
| 2013 | UU | WEB_UU_AN2013.txt | 1.1 MB | web_UU_2013.csv | 2021 |
| 2014 | BL | WEB_BL_BS_SL_AN2014.txt | 62.0 MB | WEB_BL_BS_SL_AN2014.csv | 2021 |
| 2014 | IR | WEB_IR_AN2014.txt | 0.0 MB | web_ir_2014.csv | 2021 |
| 2014 | UU | WEB_UU_AN2014.txt | 1.1 MB | web_uu_2014.csv | 2021 |
| 2015 | BL | WEB_BL_BS_SL_AN2015.txt | 13.3 MB | WEB_BL_BS_SL_AN2015.csv | 2020 |
| 2015 | IR | WEB_IR_AN2015.txt | 0.0 MB | web_ir_2015.csv | 2021 |
| 2015 | UU | WEB_UU_AN2015.txt | 49.3 MB | web_uu_2015.csv | 2021 |
| 2016 | BL | WEB_BL_BS_SL_AN2016.txt | 11.0 MB | WEB_BL_BS_SL_AN2016.csv | 2021 |
| 2016 | IR | WEB_IR_AN2016.txt | 0.0 MB | WEB_IR_2016.csv | 2021 |
| 2016 | UU | WEB_UU_AN2016.txt | 54.0 MB | WEB_UU_AN2016.csv | 2021 |
| 2017 | BL | WEB_BL_BS_SL_AN2017.txt | 10.0 MB | WEB_BL_BS_SL_AN2017.csv | 2020 |
| 2017 | IR | WEB_IR_AN2017.txt | 0.0 MB | WEB_IR_2017.csv | 2021 |
| 2017 | UU | WEB_UU_AN2017.txt | 57.9 MB | WEB_UU_AN2017.csv | 2021 |
| 2018 | BL | WEB_BL_BS_SL_AN2018.txt | 9.2 MB | webblbsslan2018.csv | 2021 |
| 2018 | IR | WEB_IR_AN2018.txt | 0.0 MB | webir2018.csv | 2021 |
| 2018 | UU | WEB_UU_AN2018.txt | 61.5 MB | webuuan2018.csv | 2021 |
| 2019 | BL | WEB_BL_BS_SL_AN2019.txt | 9.0 MB | web_bl_bs_sl_an2019.csv | 2021 |
| 2019 | IR | WEB_IR_AN2019.txt | 0.0 MB | web_ir_2019.csv | 2021 |
| 2019 | UU | WEB_UU_AN2019.txt | 65.5 MB | web_uu_an2019.csv | 2021 |
| 2020 | BL | WEB_BL_BS_SL_AN2020.txt | 8.7 MB | WEB_BL_BS_SL_AN2020.csv | 2021 |
| 2020 | IR | WEB_IR_AN2020.txt | 0.0 MB | WEB_IR_2020.csv | 2021 |
| 2020 | UU | WEB_UU_AN2020.txt | 68.0 MB | WEB_UU_AN2020.csv | 2021 |
| 2021 | BL | WEB_BL_BS_SL_AN2021.txt | 8.2 MB | WEB_BL_BS_SL_AN2021.csv | 2021 |
| 2021 | IR | WEB_IR_AN2021.txt | 0.0 MB | WEB_IR_2021.csv | 2021 |
| 2021 | UU | WEB_UU_AN2021.txt | 66.1 MB | WEB_UU_AN2021.csv | 2021 |
| 2022 | BL | WEB_BL_BS_SL_AN2022.txt | 8.8 MB | WEB_BL_BS_SL_AN2022.csv | 2022 |
| 2022 | IR | WEB_IR_AN2022.txt | 0.0 MB | WEB_IR_2022.csv | 2022 |
| 2022 | UU | WEB_UU_AN2022.txt | 73.4 MB | WEB_UU_AN2022.csv | 2022 |
| 2023 | BL | WEB_BL_BS_SL_AN2023 - actualizat | 9.5 MB | WEB_BL_BS_SL_AN2023.csv | 2023 |
| 2023 | IR | WEB_IR_AN2023 - actualizat | 0.0 MB | WEB_IR_2023.csv | 2023 |
| 2023 | UU | WEB_UU_AN2023 - actualizat | 80.5 MB | WEB_UU_AN2023.csv | 2023 |
| 2024 | BL | WEB_BL_BS_SL_AN2024.txt | 9.5 MB | WEB_BL_BS_SL_AN2024.csv | 2024_actualizat |
| 2024 | IR | WEB_IR_AN2024.txt | 0.0 MB | WEB_IR_2024.csv | 2024_actualizat |
| 2024 | UU | WEB_UU_AN2024.txt | 82.8 MB | WEB_UU_AN2024.csv | 2024_actualizat |
| 2025 | BL | WEB_BL_BS_SL_AN2025.txt | 8.6 MB | WEB_BL_BS_SL_AN2025.csv | 2025 |
| 2025 | IR | WEB_IR_AN2025.txt | 0.0 MB | WEB_IR_2025.csv | 2025 |
| 2025 | UU | WEB_UU_AN2025.txt | 79.5 MB | WEB_UU_AN2025.csv | 2025 |

Total: 94 files, 1.09 GB known plus 3 data files of unknown size (2008–2010, estimated 60 MB each), about 1.3 GB.
## S02.4 — older ONRC registry snapshots

data.gov.ro has 62 registry snapshots from 21.10.2013 to 19.10.2026 (about quarterly until 2025, monthly since). All of them total about 45 GB. Formats change over time: 2013–2025 publish separate files for struck-off and active companies, with and without a registered office; from June 2025 the current `OD_FIRME` layout.

Proposed: one snapshot per year, the last of each year, company files only (no representatives or branches).

| Year | Snapshot | Files | Size |
| --- | --- | --- | --- |
| 2013 | 21.10.2013 | 5 | 206 MB |
| 2014 | late 2014 | 6 | 216 MB |
| 2015 | 16.11.2015 | 6 | ? |
| 2016 | 31.10.2016 | 6 | ? |
| 2017 | 31.10.2017 | 10 | ? |
| 2018 | 01.11.2018 | 10 | ? |
| 2019 | 20.11.2019 | 10 | ? |
| 2020 | 08.12.2020 | 12 | 577 MB |
| 2021 | 09.12.2021 | 4 | 868 MB |
| 2022 | 07.10.2022 | 4 | 902 MB |
| 2023 | 07.10.2023 | 4 | 941 MB |
| 2024 | 18.12.2024 | 4 | 964 MB |
| 2025 | 08.12.2025 | 1 | 675 MB |
| 2026 | 19.10.2026 | — | already downloaded |

About 6 GB known plus five snapshots of unknown size (2015–2019, estimated 300–500 MB each): about 8 GB. Disk has about 700 GB free.
