using System.Globalization;
using System.Text;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Onrc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusinessIntelligence.Modules.Search;

/// <summary>A search or browse request as it appears in the page URL. All filters are optional and combine.</summary>
public sealed record SearchRequest(
    string? Query = null,
    short? County = null,
    int? Uat = null,
    string? Caen = null,
    short CaenVersion = CompanySearch.DefaultCaenVersion,
    string? Lifecycle = null,
    int Page = 1,
    bool IncludeHits = true,
    FinancialFilter? Financials = null,
    string? Signal = null);

/// <summary>
/// Ranges on the latest published financial year (bounds inclusive, any may be null) and an order of the results. A
/// range leaves out companies without a statement for that year or with the value blank.
/// </summary>
public sealed record FinancialFilter(decimal? TurnoverMin = null, decimal? TurnoverMax = null, int? EmployeesMin = null, int? EmployeesMax = null,
    string? Sort = null)
{
    public const string ByTurnover = "turnover";
    public const string ByEmployees = "employees";

    public bool HasRange => TurnoverMin is not null || TurnoverMax is not null || EmployeesMin is not null || EmployeesMax is not null;

    public bool IsEmpty => !HasRange && Sort is null;
}

public sealed record SearchHit(
    string CompanyKey,
    long? Cui,
    string RegistrationNumber,
    string? Name,
    string? LegalForm,
    string Lifecycle,
    DateOnly? FirstRegistrationDate,
    string? Place,
    string? County,
    bool IdentifierMatch,
    string? EarlierName,
    short? FinancialYear = null,
    decimal? Turnover = null,
    int? Employees = null);

public sealed record FacetItem(string Value, string Label, long Count);

public sealed record CaenNode(string Code, string Level, string? Name);

public sealed record SearchResult
{
    /// <summary>False until the search tables have been built; pages then explain that no data is imported.</summary>
    public bool Ready { get; init; }

    /// <summary>The name part of the query is too short to search; nothing was searched.</summary>
    public bool QueryTooShort { get; init; }
    public DateTimeOffset? BuiltAt { get; init; }
    public long Total { get; init; }
    public int Page { get; init; }
    public int Pages { get; init; }
    public IReadOnlyList<SearchHit> Hits { get; init; } = [];
    public IReadOnlyList<FacetItem> Lifecycles { get; init; } = [];

    /// <summary>Counties when no county is selected, otherwise the county's UATs (cities and communes).</summary>
    public IReadOnlyList<FacetItem> Places { get; init; } = [];

    /// <summary>The CAEN codes one level below the selected one, with their companies.</summary>
    public IReadOnlyList<FacetItem> Activities { get; init; } = [];
    public IReadOnlyList<CaenNode> CaenPath { get; init; } = [];
    public string? CountyName { get; init; }
    public string? UatName { get; init; }

    /// <summary>The financial year the turnover and employee filters use; null when no statements were imported.</summary>
    public short? FinancialYear { get; init; }
}

/// <summary>
/// Finds companies by CUI, registry number or name and counts them by lifecycle, place and CAEN code. Exact
/// identifiers come first, then names that are equal to the query, start with it, have a word starting with it, contain
/// all its words, and finally names that are only similar (trigram similarity). Browsing without a name or locality
/// reads the precomputed counts.
/// </summary>
public sealed class CompanySearch(AppDbContext database)
{
    public const short DefaultCaenVersion = 3;
    public const int PageSize = 20;

    /// <summary>Deeper pages are not served; the page asks for a narrower search instead.</summary>
    public const int MaxPage = 250;

    public static readonly IReadOnlyList<short> CaenVersions = [3, 2];

    /// <summary>The <see cref="SearchRequest.Signal"/> for companies that were checked and have no warning signal.</summary>
    public const string NoSignal = "none";

    /// <summary>pg_trgm word similarity for the approximate tier: one or two wrong letters in a word still match.</summary>
    public const double SimilarityThreshold = 0.5;

    /// <summary>Name queries shorter than this (after folding) match too much to be useful.</summary>
    public const int MinimumNameLength = 2;

    /// <summary>The approximate tier adds at most this many companies, the closest first.</summary>
    public const int MaxSimilar = 100;

    /// <summary>Lifecycle values in display order, with the label and URL value the pages use.</summary>
    public static readonly IReadOnlyList<(string Value, string Slug, string Label)> LifecycleLabels =
    [
        (Lifecycle.Active, "activa", "Activă"),
        (Lifecycle.Suspended, "suspendata", "Suspendată"),
        (Lifecycle.Dissolution, "dizolvare", "În dizolvare"),
        (Lifecycle.Insolvency, "insolventa", "În insolvență"),
        (Lifecycle.StruckOff, "radiata", "Radiată"),
        (Lifecycle.Other, "alta", "Altă stare"),
        (Lifecycle.Unknown, "necunoscuta", "Stare necunoscută"),
    ];

    public async Task<SearchResult> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var build = await database.SearchBuilds.AsNoTracking().OrderByDescending(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        if (build is null)
        {
            return new SearchResult();
        }

        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        var query = SearchKeys.Parse(request.Query);
        if (query.Cui is null && query.RegistrationNumber is null && query.NameKey is { Length: < MinimumNameLength })
        {
            return new SearchResult { Ready = true, BuiltAt = build.BuiltAt, QueryTooShort = true };
        }

        var hasQuery = query.Cui is not null || query.RegistrationNumber is not null || query.NameKey is not null;
        var path = request.Caen is null ? [] : await CaenPathAsync(connection, request.CaenVersion, request.Caen, cancellationToken);
        var caen = path.Count > 0 ? path[^1].Code : null;
        var token = caen is null ? null : $"{request.CaenVersion}:{caen}";
        var page = Math.Clamp(request.Page, 1, MaxPage);

        var financials = request.Financials ?? new FinancialFilter();
        var signal = request.Signal is { } chosen && (chosen == NoSignal || Risk.RiskSignals.All.Any(item => item.Key == chosen)) ? chosen : null;
        var filter = new SqlFilter(query, request.County, request.Uat, token, request.Lifecycle, financials, signal);
        var children = await CaenChildrenAsync(connection, request.CaenVersion, path.Count > 0 ? path[^1] : null, cancellationToken);
        List<HitRow> rows;
        long total;
        Dictionary<string, Dictionary<string, long>> facets;
        // The stored counts cover county, lifecycle and CAEN only; a locality or a financial filter is counted live.
        if (hasQuery || request.Uat is not null || !financials.IsEmpty || signal is not null)
        {
            (rows, total, facets) = await LiveAsync(connection, filter, hasQuery, page, request.CaenVersion, children.Keys, cancellationToken);
        }
        else
        {
            rows = request.IncludeHits ? await BrowseAsync(connection, filter, page, cancellationToken) : [];
            total = await CountAsync(connection, request.County ?? 0, request.Lifecycle ?? string.Empty, token ?? string.Empty, cancellationToken);
            facets = await StoredFacetsAsync(connection, request.County, request.Lifecycle, token, request.CaenVersion, children.Keys, cancellationToken);
        }

        var counties = await PlaceNamesAsync(connection, "SELECT county_number, name FROM registry.siruta WHERE level = 1", null, cancellationToken, counties: true);
        var uats = request.County is { } county
            ? await PlaceNamesAsync(connection, "SELECT code, name FROM registry.siruta WHERE level = 2 AND county_number = @county", county, cancellationToken)
            : [];
        var places = request.County is null
            ? Facet(facets.GetValueOrDefault("county"), value => counties.GetValueOrDefault(Parse(value)))
            : Facet(facets.GetValueOrDefault("uat"), value => uats.GetValueOrDefault(Parse(value)));
        return new SearchResult
        {
            Ready = true,
            BuiltAt = build.BuiltAt,
            Total = total,
            Page = page,
            Pages = (int)Math.Min(MaxPage, (total + PageSize - 1) / PageSize),
            Hits = await LabelHitsAsync(connection, rows, counties, cancellationToken),
            Lifecycles = [.. LifecycleLabels
                .Select(item => new FacetItem(item.Value, item.Label, facets.GetValueOrDefault("lifecycle")?.GetValueOrDefault(item.Value) ?? 0))
                .Where(item => item.Count > 0 || item.Value == request.Lifecycle)],
            Places = [.. places.OrderBy(item => item.Label, StringComparer.Create(CultureInfo.GetCultureInfo("ro-RO"), false))],
            Activities = [.. Facet(facets.GetValueOrDefault("caen"), value => children.GetValueOrDefault(value[(value.IndexOf(':', StringComparison.Ordinal) + 1)..]))
                .OrderBy(item => item.Value, StringComparer.Ordinal)],
            CaenPath = path,
            CountyName = request.County is { } selected ? counties.GetValueOrDefault(selected) : null,
            UatName = request.Uat is { } uat ? uats.GetValueOrDefault(uat) : null,
            FinancialYear = FinancialYearOf(build),
        };

        static int Parse(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static short? FinancialYearOf(SearchBuild build)
    {
        using var report = System.Text.Json.JsonDocument.Parse(build.Report);
        return report.RootElement.TryGetProperty("financialYear", out var year) && year.TryGetInt16(out var value) ? value : null;
    }

    private static List<FacetItem> Facet(Dictionary<string, long>? counts, Func<string, string?> label) =>
        counts is null ? [] : [.. counts.Where(item => item.Value > 0).Select(item => new FacetItem(item.Key, label(item.Key) ?? item.Key, item.Value))];

    /// <summary>The WHERE clauses and parameters for one request, shared by the result list and the live counts.</summary>
    private sealed class SqlFilter(SearchQuery query, short? county, int? uat, string? caenToken, string? lifecycle, FinancialFilter financials, string? signal)
    {
        public SearchQuery Query { get; } = query;
        public string? Lifecycle { get; } = lifecycle;
        public FinancialFilter Financials { get; } = financials;

        /// <summary>The order of a sorted result list, after the chosen measure: companies without it last, then by name.</summary>
        public string? SortOrder => Financials.Sort switch
        {
            FinancialFilter.ByTurnover => "c.turnover DESC NULLS LAST, c.name_key, c.company_key",
            FinancialFilter.ByEmployees => "c.employees DESC NULLS LAST, c.name_key, c.company_key",
            _ => null,
        };

        /// <summary>Clauses on <c>search.companies c</c>, without the lifecycle (counted as its own facet).</summary>
        public string Where()
        {
            var clauses = new List<string> { "true" };
            if (county is not null) { clauses.Add("c.county_number = @county"); }
            if (uat is not null) { clauses.Add("c.uat_code = @uat"); }
            if (caenToken is not null) { clauses.Add("c.caen @> ARRAY[@caen]"); }
            if (Financials.TurnoverMin is not null) { clauses.Add("c.turnover >= @turnoverMin"); }
            if (Financials.TurnoverMax is not null) { clauses.Add("c.turnover <= @turnoverMax"); }
            if (Financials.EmployeesMin is not null) { clauses.Add("c.employees >= @employeesMin"); }
            if (Financials.EmployeesMax is not null) { clauses.Add("c.employees <= @employeesMax"); }
            // "No signal" covers only companies the signals are built for: with a CUI and not struck off.
            if (signal == NoSignal)
            {
                clauses.Add("c.cui IS NOT NULL AND c.lifecycle <> 'StruckOff' AND NOT EXISTS (SELECT 1 FROM risk.signals r WHERE r.company_key = c.company_key)");
            }
            else if (signal is not null)
            {
                clauses.Add("EXISTS (SELECT 1 FROM risk.signals r WHERE r.company_key = c.company_key AND r.signal = @signal)");
            }
            return string.Join(" AND ", clauses);
        }

        public string LifecycleClause => Lifecycle is null ? "true" : "c.lifecycle = @lifecycle";

        /// <summary>
        /// The <c>hits</c> CTE: every matching company once, with its best tier. Each query word must start a word of the
        /// name. Words shorter than three letters have no trigrams, so they are tested with strpos to keep them out of
        /// the trigram index scan; a query without a longer word matches name prefixes only.
        /// </summary>
        public string Hits()
        {
            var parts = new List<string>();
            if (Query.Cui is not null)
            {
                parts.Add("SELECT company_key, -1 AS tier, NULL::text AS earlier, 1::real AS similarity FROM search.companies WHERE cui = @cui");
            }

            if (Query.RegistrationNumber is not null)
            {
                parts.Add("SELECT company_key, -1 AS tier, NULL::text AS earlier, 1::real AS similarity FROM search.registration_numbers WHERE number_key = @number");
            }

            if (Query.NameKey is not null)
            {
                var words = Query.Words.Select((word, index) => word.Length >= 3
                    ? $"(n.name_key LIKE @w{index} OR n.name_key LIKE @wi{index})"
                    : $"strpos(' ' || n.name_key, @wi{index}) > 0").ToList();
                var allWords = words.Count == 0 ? "false" : string.Join(" AND ", words);
                var condition = Query.Words.Any(word => word.Length >= 3) ? allWords : "n.name_key LIKE @prefix";
                var tier = $"CASE WHEN n.name_key = @key THEN 0 WHEN n.name_key LIKE @prefix THEN 1 WHEN n.name_key LIKE @wordStart THEN 2 ELSE 3 END";
                parts.Add(Names(tier, condition));
            }

            return $"""
                hits AS (
                    SELECT DISTINCT ON (company_key) company_key, tier, earlier, similarity
                    FROM ({string.Join(" UNION ALL ", parts)}) found
                    ORDER BY company_key, tier, earlier NULLS FIRST)
                """;
        }

        /// <summary>
        /// The closest names that only resemble the query (trigram word similarity of its words, legal forms left out),
        /// for companies not already matched. Run only when the exact tiers fill less than a page.
        /// </summary>
        public static string SimilarHits() => $"""
            hits AS (
                SELECT * FROM ({Names("4", "@similar <% n.name_key AND NOT EXISTS (SELECT 1 FROM matched m WHERE m.company_key = n.company_key)",
                    "word_similarity(@similar, n.name_key)")}) close_names
                ORDER BY similarity DESC, company_key
                LIMIT {MaxSimilar})
            """;

        public bool CanBeSimilar => Query.NameKey is not null && Query.Words.Any(word => word.Length >= 3);

        private static string Names(string tier, string condition, string similarity = "similarity(n.name_key, @key)") => $"""
            SELECT * FROM (
                SELECT DISTINCT ON (n.company_key) n.company_key, {tier} AS tier, CASE WHEN n.current THEN NULL ELSE n.name END AS earlier,
                    {similarity} AS similarity
                FROM search.names n
                WHERE {condition}
                ORDER BY n.company_key, {tier}, n.current DESC, {similarity} DESC) names
            """;

        public void Bind(NpgsqlCommand command)
        {
            Add("county", county);
            Add("uat", uat);
            Add("caen", caenToken);
            Add("lifecycle", Lifecycle);
            Add("cui", Query.Cui);
            Add("number", Query.RegistrationNumber);
            Add("turnoverMin", Financials.TurnoverMin);
            Add("turnoverMax", Financials.TurnoverMax);
            Add("employeesMin", Financials.EmployeesMin);
            Add("employeesMax", Financials.EmployeesMax);
            Add("signal", signal == NoSignal ? null : signal);
            if (Query.NameKey is { } key)
            {
                command.Parameters.AddWithValue("key", key);
                command.Parameters.AddWithValue("prefix", key + "%");
                command.Parameters.AddWithValue("wordStart", "% " + key + "%");
                command.Parameters.AddWithValue("similar", string.Join(' ', Query.Words));
                for (var index = 0; index < Query.Words.Count; index++)
                {
                    var word = Query.Words[index];
                    command.Parameters.AddWithValue("wi" + index, word.Length >= 3 ? "% " + word + "%" : " " + word);
                    if (word.Length >= 3)
                    {
                        command.Parameters.AddWithValue("w" + index, word + "%");
                    }
                }
            }

            void Add(string name, object? value)
            {
                if (value is not null)
                {
                    command.Parameters.AddWithValue(name, value);
                }
            }
        }
    }

    private sealed record HitRow(string CompanyKey, long? Cui, string RegistrationNumber, string? Name, string? LegalForm, string Lifecycle,
        DateOnly? FirstRegistrationDate, short? CountyNumber, int? UatCode, int? LocalityCode, short? FinancialYear, decimal? Turnover, int? Employees,
        bool IdentifierMatch, string? EarlierName);

    private const string HitColumns =
        "d.company_key, d.cui, d.registration_number, d.name, d.legal_form, d.lifecycle, d.first_registration_date, d.county_number, d.uat_code, d.locality_code, "
        + "d.financial_year, d.turnover, d.employees";

    private const string MatchedColumns = "c.name_key, c.county_number, c.uat_code, c.lifecycle, c.caen, c.turnover, c.employees";

    /// <summary>
    /// With a name or identifier query, or a locality: the matching companies are collected once into a temporary table,
    /// then the page, the total and every count read it.
    /// </summary>
    private static async Task<(List<HitRow> Rows, long Total, Dictionary<string, Dictionary<string, long>> Facets)> LiveAsync(
        NpgsqlConnection connection, SqlFilter filter, bool hasQuery, int page, short caenVersion, IEnumerable<string> children, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var source = hasQuery
            ? $"""
                WITH {filter.Hits()}
                SELECT h.company_key, h.tier, h.earlier, h.similarity, {MatchedColumns}
                FROM hits h JOIN search.companies c ON c.company_key = h.company_key
                WHERE {filter.Where()}
                """
            : $"""
                SELECT c.company_key, 0 AS tier, NULL::text AS earlier, 0::real AS similarity, {MatchedColumns}
                FROM search.companies c
                WHERE {filter.Where()}
                """;
        await using (var create = new NpgsqlCommand($"CREATE TEMP TABLE matched ON COMMIT DROP AS {source}", connection, transaction))
        {
            filter.Bind(create);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        if (hasQuery && filter.CanBeSimilar && await CountMatchedAsync(connection, transaction, cancellationToken) < PageSize)
        {
            await using (var threshold = new NpgsqlCommand(
                $"SET LOCAL pg_trgm.word_similarity_threshold = {SimilarityThreshold.ToString(CultureInfo.InvariantCulture)}", connection, transaction))
            {
                await threshold.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var similar = new NpgsqlCommand($"""
                WITH {SqlFilter.SimilarHits()}
                INSERT INTO matched
                SELECT h.company_key, h.tier, h.earlier, h.similarity, {MatchedColumns}
                FROM hits h JOIN search.companies c ON c.company_key = h.company_key
                WHERE {filter.Where()}
                """, connection, transaction);
            filter.Bind(similar);
            await similar.ExecuteNonQueryAsync(cancellationToken);
        }

        // Ranked results: identifiers, then name tiers, current names before earlier ones, active companies first;
        // approximate matches by closeness alone.
        var order = filter.SortOrder ?? (hasQuery
            ? "c.tier, c.earlier IS NOT NULL, CASE WHEN c.tier = 4 THEN -c.similarity ELSE 0 END, c.lifecycle <> 'Active', c.similarity DESC, c.name_key, c.company_key"
            : "c.name_key, c.company_key");
        var rows = new List<HitRow>();
        // The temporary table has no statistics; joining only the page keeps the planner from scanning every company.
        await using (var command = new NpgsqlCommand($"""
            SELECT {HitColumns}, p.tier < 0, p.earlier
            FROM (SELECT c.* FROM matched c WHERE {filter.LifecycleClause} ORDER BY {order} LIMIT @limit OFFSET @offset) p
            JOIN search.companies d ON d.company_key = p.company_key
            ORDER BY {order.Replace("c.", "p.", StringComparison.Ordinal)}
            """, connection, transaction))
        {
            filter.Bind(command);
            command.Parameters.AddWithValue("limit", PageSize);
            command.Parameters.AddWithValue("offset", (page - 1) * PageSize);
            await ReadHitsAsync(command, rows, cancellationToken);
        }

        var tokens = children.Select(code => $"{caenVersion}:{code}").ToArray();
        Dictionary<string, Dictionary<string, long>> facets;
        await using (var command = new NpgsqlCommand($"""
            SELECT 'total', '', count(*) FROM matched c WHERE {filter.LifecycleClause}
            UNION ALL SELECT 'lifecycle', c.lifecycle, count(*) FROM matched c GROUP BY c.lifecycle
            UNION ALL SELECT 'county', c.county_number::text, count(*) FROM matched c WHERE {filter.LifecycleClause} AND c.county_number IS NOT NULL GROUP BY c.county_number
            UNION ALL SELECT 'uat', c.uat_code::text, count(*) FROM matched c WHERE {filter.LifecycleClause} AND c.uat_code IS NOT NULL GROUP BY c.uat_code
            UNION ALL SELECT 'caen', t, count(*) FROM matched c CROSS JOIN LATERAL unnest(c.caen) t WHERE {filter.LifecycleClause} AND t = ANY(@tokens) GROUP BY t
            """, connection, transaction))
        {
            filter.Bind(command);
            command.Parameters.AddWithValue("tokens", tokens);
            facets = await ReadFacetsAsync(command, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (rows, facets.GetValueOrDefault("total")?.GetValueOrDefault("") ?? 0, facets);
    }

    private static async Task<long> CountMatchedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM matched", connection, transaction);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    /// <summary>Browsing by county, CAEN and lifecycle: the page in name order; the total comes from <c>search.counts</c>.</summary>
    private static async Task<List<HitRow>> BrowseAsync(NpgsqlConnection connection, SqlFilter filter, int page, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT {HitColumns}, false, NULL::text
            FROM search.companies d
            WHERE {filter.Where().Replace("c.", "d.", StringComparison.Ordinal)} AND {filter.LifecycleClause.Replace("c.", "d.", StringComparison.Ordinal)}
            ORDER BY d.name_key, d.company_key
            LIMIT @limit OFFSET @offset
            """, connection);
        filter.Bind(command);
        command.Parameters.AddWithValue("limit", PageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * PageSize);
        var rows = new List<HitRow>();
        await ReadHitsAsync(command, rows, cancellationToken);
        return rows;
    }

    private static async Task ReadHitsAsync(NpgsqlCommand command, List<HitRow> rows, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new HitRow(
                reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetString(2), Text(reader, 3), Text(reader, 4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6), reader.IsDBNull(7) ? null : reader.GetInt16(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt16(10), reader.IsDBNull(11) ? null : reader.GetDecimal(11), reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.GetBoolean(13), Text(reader, 14)));
        }
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, short county, string lifecycle, string token, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT companies FROM search.counts WHERE county_number = @county AND lifecycle = @lifecycle AND caen = @caen", connection);
        command.Parameters.AddWithValue("county", county);
        command.Parameters.AddWithValue("lifecycle", lifecycle);
        command.Parameters.AddWithValue("caen", token);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
    }

    /// <summary>Counts from <c>search.counts</c>; only the cities of a selected county are counted live, within that county.</summary>
    private static async Task<Dictionary<string, Dictionary<string, long>>> StoredFacetsAsync(NpgsqlConnection connection, short? county, string? lifecycle,
        string? token, short caenVersion, IEnumerable<string> children, CancellationToken cancellationToken)
    {
        var sql = new StringBuilder("""
            SELECT 'lifecycle', lifecycle, companies FROM search.counts WHERE county_number = @county AND lifecycle <> '' AND caen = @caen
            UNION ALL SELECT 'caen', caen, companies FROM search.counts WHERE county_number = @county AND lifecycle = @lifecycle AND caen = ANY(@tokens)
            """);
        sql.AppendLine();
        sql.Append(county is null
            ? "UNION ALL SELECT 'county', county_number::text, companies FROM search.counts WHERE county_number > 0 AND lifecycle = @lifecycle AND caen = @caen"
            : $"""
                UNION ALL SELECT 'uat', c.uat_code::text, count(*) FROM search.companies c
                WHERE c.county_number = @county AND c.uat_code IS NOT NULL{(token is null ? "" : " AND c.caen @> ARRAY[@caen]")}{(lifecycle is null ? "" : " AND c.lifecycle = @lifecycle")}
                GROUP BY c.uat_code
                """);
        await using var command = new NpgsqlCommand(sql.ToString(), connection);
        command.Parameters.AddWithValue("county", county ?? (short)0);
        command.Parameters.AddWithValue("lifecycle", lifecycle ?? string.Empty);
        command.Parameters.AddWithValue("caen", token ?? string.Empty);
        command.Parameters.AddWithValue("tokens", children.Select(code => $"{caenVersion}:{code}").ToArray());
        return await ReadFacetsAsync(command, cancellationToken);
    }

    private static async Task<Dictionary<string, Dictionary<string, long>>> ReadFacetsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var facets = new Dictionary<string, Dictionary<string, long>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var facet = reader.GetString(0);
            if (!facets.TryGetValue(facet, out var counts))
            {
                facets[facet] = counts = [];
            }

            counts[reader.GetString(1)] = Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture);
        }

        return facets;
    }

    /// <summary>The selected code and every level above it, section first; empty when the code is not in that CAEN version.</summary>
    private static async Task<List<CaenNode>> CaenPathAsync(NpgsqlConnection connection, short version, string code, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT p.code, p.level, p.name
            FROM registry.caen e
            JOIN registry.caen p ON p.version = e.version AND p.code IN (e.section, e.division, e."group", e.code)
            WHERE e.version = @version AND e.code = @code AND e.level IN ('section', 'division', 'group', 'class')
            ORDER BY CASE p.level WHEN 'section' THEN 0 WHEN 'division' THEN 1 WHEN 'group' THEN 2 ELSE 3 END
            """, connection);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("code", code.Trim().ToUpperInvariant());
        var path = new List<CaenNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            path.Add(new CaenNode(reader.GetString(0), reader.GetString(1), Text(reader, 2)));
        }

        return path;
    }

    private static async Task<Dictionary<string, string?>> CaenChildrenAsync(NpgsqlConnection connection, short version, CaenNode? parent, CancellationToken cancellationToken)
    {
        var condition = parent?.Level switch
        {
            null => "level = 'section'",
            "section" => "level = 'division' AND section = @parent",
            "division" => "level = 'group' AND division = @parent",
            "group" => "level = 'class' AND \"group\" = @parent",
            _ => null,
        };
        if (condition is null)
        {
            return [];
        }

        await using var command = new NpgsqlCommand($"SELECT code, name FROM registry.caen WHERE version = @version AND {condition}", connection);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("parent", parent?.Code ?? string.Empty);
        var children = new Dictionary<string, string?>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            children[reader.GetString(0)] = Text(reader, 1);
        }

        return children;
    }

    private static async Task<Dictionary<int, string>> PlaceNamesAsync(NpgsqlConnection connection, string sql, short? countyNumber, CancellationToken cancellationToken, bool counties = false)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (countyNumber is not null)
        {
            command.Parameters.AddWithValue("county", countyNumber.Value);
        }

        var names = new Dictionary<int, string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names[Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)] = PlaceNames.Display(reader.GetString(1), counties);
        }

        return names;
    }

    private static async Task<List<SearchHit>> LabelHitsAsync(NpgsqlConnection connection, List<HitRow> rows, Dictionary<int, string> counties, CancellationToken cancellationToken)
    {
        var codes = rows.SelectMany(row => new[] { row.LocalityCode, row.UatCode }).OfType<int>().Distinct().ToArray();
        var places = new Dictionary<int, string>();
        if (codes.Length > 0)
        {
            await using var command = new NpgsqlCommand("SELECT code, name FROM registry.siruta WHERE code = ANY(@codes)", connection);
            command.Parameters.AddWithValue("codes", codes);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                places[reader.GetInt32(0)] = PlaceNames.Display(reader.GetString(1));
            }
        }

        return [.. rows.Select(row => new SearchHit(
            row.CompanyKey, row.Cui, row.RegistrationNumber, row.Name, row.LegalForm, row.Lifecycle, row.FirstRegistrationDate,
            (row.LocalityCode ?? row.UatCode) is { } place ? places.GetValueOrDefault(place) : null,
            row.CountyNumber is { } county ? counties.GetValueOrDefault(county) : null,
            row.IdentifierMatch, row.EarlierName, row.FinancialYear, row.Turnover, row.Employees))];
    }

    private static string? Text(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
