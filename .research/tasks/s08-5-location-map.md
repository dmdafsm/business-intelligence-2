# S08.5 Location map

Date: 2026-10-25. Branch: codex/representatives. Operation: [profile open data](../../docs/engineering/profile-open-data.md).

## What was built

- Table `registry.area_outlines`, migration `AreaOutlines`; `registry import boundaries-ancpi-…` for the county and UAT shapefiles (ANCPI 07.03.2024, CC BY 4.0).
- Outlines simplified (1,500 m counties, 350 m UATs) into SVG paths in one Stereo70 frame; parser `ancpi-shapefile-v2`.
- `_LocationMap` partial: Romania with the county highlighted and a county close-up with the company's UAT; server-rendered, no JavaScript.

## Tests

Shapefile to outline in the national frame; browser check of the profile map.

## Limits

The map shows the locality, not the street address.
