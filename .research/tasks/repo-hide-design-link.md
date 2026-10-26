# REPO Design link removed from the navigation

Date: 2026-10-26.

- The "Design" destination is gone from the navigation rail and bar; search is the only destination. `/Design` still works by URL in Development (404 elsewhere) as the developer reference.
- `NavigationDestination.DevelopmentOnly` was only used by that link and is removed.
- The desktop navigation test now checks that no "Design" link is shown (the test portal runs in Development).
- `python3 scripts/verify.py`: PASS (147 backend, 28 browser tests).
