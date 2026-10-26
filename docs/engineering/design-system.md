# Design system

The UI follows [Material 3](https://m3.material.io), implemented in this repository as CSS tokens and Razor markup. There is no frontend package, bundler or TypeScript, and every page works without JavaScript.

## Files

| File | Contents |
| --- | --- |
| [tokens.css](../../src/BusinessIntelligence.Host/wwwroot/css/tokens.css) | System tokens: color roles (light and dark), type scale, shape, elevation, state opacities, motion, spacing |
| [components.css](../../src/BusinessIntelligence.Host/wwwroot/css/components.css) | Components: buttons, icon button, search bar, outlined text field and select, chips (filter and input), cards, list, data table, tabs, status label, pagination, facts list, source note, charts, empty state |
| [layout.css](../../src/BusinessIntelligence.Host/wwwroot/css/layout.css) | App shell (navigation rail, navigation bar, top app bar) and page section helpers |
| [_Layout.cshtml](../../src/BusinessIntelligence.Host/Pages/Shared/_Layout.cshtml) | Shell markup, skip link, icon sprite |
| [Icons.cs](../../src/BusinessIntelligence.Host/Components/Icons.cs) | Icon paths; `<md-icon name="search" />` renders one |
| [Navigation.cs](../../src/BusinessIntelligence.Host/Components/Navigation.cs) | Navigation destinations |

In Development, `/Design` shows every component, the color roles and the type scale. It is a developer reference: open it by URL; the navigation has no link to it.

## Rules

- Use `--md-sys-*` tokens only. Do not hard-code colors, font sizes, radii or shadows in page styles. Inline `style` attributes are blocked by the Content Security Policy.
- Use M3 roles by meaning: `primary` for the main action, `secondary-container` for selected states, `surface-container-*` for tonal layering, `error` only for errors.
- Each window size class has its navigation: compact (< 600px) uses the bottom navigation bar; medium and expanded use the navigation rail. Keep three to five destinations.
- Interactive elements get the M3 state layer (8% hover, 10% focus/pressed) and a visible focus outline.
- Numbers in tables are right-aligned with tabular figures. Missing values are labeled (for example "nedepus") and styled with `md-missing`, never shown as 0.
- Selected filters are links with `aria-current="true"` (chips and list items) and keep state in the URL, so they work without JavaScript. Applied filters also show as input chips whose link removes them.
- Charts are server-rendered SVG (`_BarChart` partial): one series, one axis, `--md-chart-positive`/`--md-chart-negative` (validated for each scheme), a table with every value next to them.
- Every section that shows imported data ends with an `md-source` note: publisher, dataset, period and license.
- A company's lifecycle is an `md-status` label: secondary container for active, tertiary container for states that need attention, neutral for struck off. Never `error`.
- Wide tables scroll inside `md-table-wrapper`; the page never scrolls horizontally at 360px.
- New components get a class `md-<name>`, use only tokens, and are added to `/Design`.

## Theme

Colors are the M3 baseline scheme (seed #6750A4). To rebrand, export a scheme from Material Theme Builder and replace both color blocks in tokens.css, keeping the token names. The scheme follows the system light/dark preference.

The typeface stack starts with Roboto Flex and Roboto, falling back to the platform UI font until the font files are self-hosted. Icons are Material Icons paths (Apache-2.0) in an inline SVG sprite.
