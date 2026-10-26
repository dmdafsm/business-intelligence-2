# S13.1 Risk signals

Date: 2026-10-26. Branch: codex/s13-risk-signals. Plan: [S13](../steps/s13-risk-signals.md).

## What was built

- **Module `Risk`**, schema `risk`, migration `RiskSignals`: tables `signals` (one row per company and signal, with its year or date, amount, ratio and threshold) and `builds`. The rules version is `signals-v1`.
- **`risk build`**: rebuilds the signals in one transaction. It is added to the ONRC, MF financials and ANAF jobs.
  - It skips struck-off companies and companies without a CUI.
  - "Not filing" applies only to active companies (not PFA/II/IF) registered before the latest financial year.
  - Locally it took 29 s and produced:

    | Signal | Companies |
    | --- | --- |
    | ANAF inactive | 484,444 |
    | No recent statement | 313,970 |
    | Negative equity | 286,112 |
    | Repeated losses | 99,728 |
    | High debt ratio (above the CAEN class P90, 2024) | 57,089 |

- **Profile**: a "Semnale de atenție" section, with a navigation tab. Each signal has its reason, year or date and source. The section states that signals are not a rating and that their absence does not mean the absence of risk.
- **Search**: a "Semnale de atenție" chip filter (`semnal=` in the URL), including "Fără semnale".
  - "Fără semnale" covers only checked companies: those with a CUI that are not struck off.
  - Any signal filter is counted live.

## Tests

- **PostgreSQL**:
  - Each rule, the rebuild idempotency and that only the latest ANAF answer counts.
  - Struck-off companies, PFA without CUI and companies in insolvency are never flagged as not filing.
  - The debt-ratio threshold comes from the benchmark.
  - Search by signal, and "no signal".
- **Browser (no JavaScript)**: the profile section with no signal, a filter chip leading to a flagged profile, and the reason text.
- **Manual check**: FAN IMPEX SRL (CUI 20130) on the real data shows four signals with their dates and amounts.

## Limits

- The national "inactiv" filter takes about 4 s locally, because about 484,000 matches are counted live.
- ANAF data covers the CUIs queried by the last harvest.
- The financial signals use the latest published year (2025). The debt-ratio comparison uses the latest benchmarked year (2024).
