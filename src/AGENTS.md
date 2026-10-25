# Application code instructions

- Apply the root rules and engineering standards. Group operations inside business modules; keep Host adapters thin and shared infrastructure small.
- Use the pinned SDK, root build/package settings and checked-in lockfiles. Add projects to the root solution and add behavioral tests in the same change.
- Do not use in-memory stores to validate PostgreSQL SQL, COPY, transactions or publication. Never run a migration automatically against an unknown database.
- UI: build pages from the Material 3 partials and tokens in the [design system](../docs/engineering/design-system.md). Do not hard-code colors, font sizes, radii or shadows in page styles. Keep filters in URLs, paginate results, and keep every page usable without JavaScript.
- No frontend package or TypeScript. Small progressive-enhancement scripts in `wwwroot` are allowed; pages must not depend on them.
