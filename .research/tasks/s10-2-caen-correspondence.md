# S10.2 CAEN Rev.3 classes for 2025

Date: 2026-10-26. Branch: codex/caen-correspondence. Plan: [S10](../steps/s10-indicators.md).

## Source

INS, "Tabel Corespondență CAEN Rev.2 - CAEN Rev.3", September 2026 edition.

- URL: https://insse.ro/cms/files/CAEN/Tabel-Corespondenta-CAEN-Rev.2-CAEN-Rev.3_septembrie-2026.xlsx
- File: 65,800 bytes, SHA-256 `19a1f2e6…c4b68e`. Pinned in the source catalog as `ins-caen-correspondence-2026-09`.
- License: OGL-ROU-1.0, the open government licence shown in the insse.ro footer.
- Search done first: data.gov.ro has no correspondence dataset, only "CAEN Clase Rev2".
- Structure:
  - One sheet, header on row 6, one row per (Rev.2 class, Rev.3 class) pair.
  - A Rev.2 class split into several Rev.3 classes is merged down column A.
  - A Rev.3 class aggregating several Rev.2 classes is merged down column C. 347 merged ranges in total.
  - Rev.3 classes marked `*` are intermediaries.
  - Detailing, aggregation and recoding are shown only by colour, which is not read.
  - The last row ("0", no name, 4689) is a stray: it is skipped and reported.
- Result: 974 pairs covering all 615 Rev.2 classes; 161 of these are split.

## What was built

- **`SimpleXlsx`**: `fillMergedCells`. Rows sit at their sheet row numbers, and every cell of a merged range takes the value of its first cell. Without it, 14 aggregated Rev.2 classes would have had no pair.
- **`registry.caen_correspondence`**: imported by `registry import ins-caen-correspondence-2026-09 <path>` (parser `ins-caen-correspondence-v2`). The migration `CaenCorrespondence` also adds `class_version`, `class_code` and `class_method` to `financials.statements`.
- **`financials benchmarks`**: before computing percentiles, sets a Rev.3 class on statements whose version is open. Published values are never changed.
- **Classification rules.** A 2025 code can mean its own Rev.3 class (if Rev.3 has it) or the Rev.3 classes its Rev.2 meaning maps to.

  | Method | When | 2025 statements |
  | --- | --- | --- |
  | `single` | one candidate | 515,873 |
  | `registry` | exactly one candidate is among the company's Rev.3 activities in the registry | 95,692 |
  | `same-code` | the declared code is a Rev.3 class in the registry and one of its own Rev.2 split classes | 34,088 |
  | `registry-rev2` | the registry is still entirely Rev.2 and the Rev.2 meaning has one class | 5,493 |
  | no class | none of the above | 267,188 |

  In total, 71% of the 918,334 statements of 2025 get a class.
- **Downstream**:
  - Benchmarks, the profile comparison and the high-debt signal use the published class, or the derived one for 2025.
  - The profile names the derived class and its method, or explains why 2024 is used.
  - Benchmarks now cover 2025 (275,510 rows).
  - The high-debt signal compares on 2025 (43,441 companies).

## Tests

- **Parser:**
  - merged cells in both directions;
  - intermediate marks;
  - the stray row;
  - without filling merged cells, the aggregated rows are lost.
- **Import:** idempotent.
- **PostgreSQL, one statement per method:**
  - an open case stays unclassified and is left out of the benchmarks;
  - published versions are untouched;
  - the profile compares on 2025, with the method and the declared code.
- **Real data:**
  - The 2025 medians agree with 2024: net margin in 4711 is 2.1% against 2.0%, and in 6210 (formerly 6201) 59% against 62%.
  - DEDEMAN is compared on 2025 in class 4752 (method `same-code`, 1,424 companies).

## Limits

- Coverage is uneven: retail Rev.2 classes split into shop and online classes, so in 4711 only 28% of 2025 statements get a class.
- The derived classes favour companies that updated their registry activities to Rev.3.
- Years before 2025 stay in Rev.2 classes. A Rev.2 → Rev.3 series across years is not built.
