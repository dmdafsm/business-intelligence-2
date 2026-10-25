# Research instructions

- All research for this project stays under `.research/`, including technical research and source investigations. Raw downloads go to ignored `.research/raw/`.
- Read the relevant step plan before adding conclusions. Keep user requirements, proposals, verified evidence and untested assumptions distinct.
- Record the date, the exact primary-source URL, what was actually read or tested, and the relevant limits. Search snippets and catalog listings do not prove what a resource contains.
- For samples, record source, period, hash, parser version, license and sanitization. Synthetic fixtures for automated tests belong under `tests/`.
- Keep step plans and implementation-map.json consistent. One short note per completed task in `tasks/` is the evidence; do not add separate evidence JSON unless a check needs machine-readable data.
- Preserve historical findings and explain corrections instead of silently rewriting them.
- Run `python3 scripts/verify.py --repository-only` after research-only edits.
