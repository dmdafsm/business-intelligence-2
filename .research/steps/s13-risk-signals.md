# S13 Risk signals

Status: completed. Depends on S04, S08.2 and S10. Added on 2026-10-26 at the maintainer's request, carried over from the original repository (F16), limited to signals the imported data supports.

## Outcome

A profile lists the warning signals that follow from the public data, each with its reason, the year or date it refers to and its source. The search can narrow results to companies with a given signal, or to companies with none.

## Signals (`signals-v1`)

| Key | Shown as | Rule | Source |
| --- | --- | --- | --- |
| `anaf_inactive` | Inactiv fiscal | The latest ANAF answer says inactive | ANAF web service |
| `no_recent_statement` | Situații financiare nedepuse | An active company (not PFA/II/IF), registered before the latest financial year, has no statement published for that year or the one before | MF |
| `negative_equity` | Capitaluri proprii negative | Equity below zero in the latest financial year | MF |
| `repeated_losses` | Pierderi trei ani la rând | A net loss in each of the last three published years | MF |
| `high_debt_ratio` | Îndatorare ridicată | The debt ratio is above P90 of the company's CAEN class (all sizes) in the latest benchmarked year | MF, S10 benchmark |

## Rules

- Signals are facts with a reason, not a score or a rating. The page says that the absence of signals does not mean the absence of risk, and names what is not checked (for example, ANAF data only covers CUIs that were queried).
- Registry states (insolvency, dissolution, struck off) already show on the profile and are filtered by state; they are not repeated as signals.
- `risk build` rebuilds the table in one transaction. It runs after the MF import, the ANAF harvest and the ONRC import.

## Acceptance

- Each signal shows its year or date and its source.
- Missing data never raises a signal: a blank equity is not negative, and a company that is not required to file is not flagged as not filing.
- The search filter lives in the URL and works without JavaScript.

## Tasks

- S13.1 Signals table, profile section and search filter — [note](../tasks/s13-1-risk-signals.md).
