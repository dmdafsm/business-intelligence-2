# REPO Profile table layout fixes

Date: 2026-10-26.

## What changed

- Opening "Toți indicatorii publicați" made the whole page scroll sideways: the profile section's implicit grid column grew to the table width. `.profile-section` now has one `minmax(0, 1fr)` column.
- Visually hidden labels ("necompletat") in far columns are absolutely positioned and escaped the table's scroll region; `.md-table-wrapper` is now `position: relative`.
- Table regions showed a vertical scrollbar next to the horizontal one; they now scroll only sideways (`overflow-y: hidden`).
- Table captions ("Cele mai recente 20") sit closer to their header row (4 px below instead of 16 px).

## Verification

- The phone profile test now opens every `details` and checks that the page does not scroll sideways and that table regions do not scroll vertically; it failed before the fix and passes after.
- `python3 scripts/verify.py`: PASS (147 backend, 28 browser tests).
- Browser check on the Docker stack (DEDEMAN profile, 1280 px and phone width, all details open): page width equals the viewport; the procurement caption sits right above its header.
