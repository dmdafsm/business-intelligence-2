# Coding standards

## Architecture and organization

The baseline is a .NET modular monolith with vertical slices: one Host (web and operator commands) and one Modules project. Module namespaces are conventions, not compiler-enforced boundaries. See the [architecture](../../.research/architecture.md).

Keep each use case's input, result, validation and handler close together. Small slices may use one file. Domain rules shared by several slices belong to the owning module; source adapters stay near the domain that understands their data. Shared contains mechanisms used in common, not a universal business model.

A module owns writes to its tables. Call an explicit operation to change another module's data. Cross-module analytical queries/projections are allowed with documented ownership and dependencies. Keep calculations in one place so every page shows the same numbers.

Use explicit mapping and ordinary dependency injection. Introduce abstractions for useful boundaries, such as external providers or time, rather than an interface for every class. EF Core already supplies change tracking and a unit of work; do not wrap it without a concrete benefit.

## C# and .NET

- Enable nullable references, SDK analyzers and warnings as errors. Follow .editorconfig and dotnet format. Use file-scoped namespaces, PascalCase types/methods, camelCase locals/parameters and `_camelCase` private instance fields.
- Prefer small, explicit types and immutable DTOs. Do not expose EF entities as public API contracts. Avoid reflection, dynamic, clever metaprogramming and new inheritance frameworks without a demonstrated need.
- Use async I/O and pass CancellationToken through database, HTTP and storage calls. Do not block with `.Result`, `.Wait()` or unnecessary Task.Run around I/O.
- Validate at boundaries; preserve domain invariants in the owning behavior. Return useful validation/conflict/not-found outcomes; log unexpected exceptions once at the responsible boundary. Do not swallow errors or return invented empty success results.
- Dispose streams/connections correctly.
- Use decimal plus currency/unit for money, explicit reference periods for reporting and UTC for operational timestamps. Inject TimeProvider where controllable time materially helps behavior tests.
- Keep comments about reasons and non-obvious constraints. Avoid comments narrating obvious code or claiming unmeasured performance.

## Persistence and import work

Use EF Core for ordinary writes and migrations; select only required columns for reads. Use no tracking for read-only entity queries and parameterized SQL for explicit analytical queries. Dynamic sort/identifier choices come from allowlists; values are parameters.

Use streaming, bounded chunks and COPY/staging for large imports. Preserve source identity, period, content hash, schema/parser version and row rejection categories. Separate unavailable, absent, non-public, zero and not-applicable values; keep CAEN classification versions explicit.

Validate before publication. Build a coherent new revision and activate it atomically; readers see either the old or the new data, never a mix. Keep transactions short and reruns idempotent.

Review generated migrations as code. Prefer additive changes compatible with the previous deployment and jobs already queued. Backfills are bounded/restartable. A destructive migration requires a concrete recovery plan and the task's authorization to execute it; a local code change does not authorize production execution.

## Razor Pages and UI

Render on the server. Every page works without JavaScript; small scripts in `wwwroot` may only enhance it. Paginate server-side and keep search and filter state in the URL. Preserve keyboard navigation, labels, empty/error states and escaped output. Never render imported strings as raw HTML.

Keep PageModels thin and call module operations. Build UI from the [design system](design-system.md): tokens for color, type, shape and elevation, and shared partials for components. Page styles must not hard-code colors, font sizes, radii or shadows.

No frontend package, bundler or TypeScript. Adding one needs a decision record first.

## Logging and dependencies

Use structured ILogger messages; avoid logs per imported row. Imports log their progress and counts at the batch level.

Centralize and pin versions, and evaluate licenses and native Linux support. Do not add infrastructure or broaden the stack as a side effect of a feature. Document material tradeoffs with the [decision template](decisions/template.md).
