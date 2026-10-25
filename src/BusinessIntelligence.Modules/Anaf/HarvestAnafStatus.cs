using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusinessIntelligence.Modules.Anaf;

/// <summary>
/// Asks the ANAF taxpayer web service about every CUI of the latest MF identification release, 100 per request and
/// at most one request per second (the service's published limits). Answers are stored after each request, so the run
/// can be stopped and resumed: CUIs already answered on or after <c>since</c> are skipped.
/// </summary>
public sealed class HarvestAnafStatus(AppDbContext database, HttpClient http, TimeProvider time, TimeSpan? interval = null, TimeSpan? backoff = null)
{
    public static readonly Uri Endpoint = new("https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva");
    public const int BatchSize = 100;
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1100);
    public const int MaximumConsecutiveFailures = 8;

    public sealed record Progress(int Requests, int Found, int NotFound, int Remaining, TimeSpan Elapsed);

    public async Task<Progress> ExecuteAsync(DateOnly since, int? maximumRequests, Action<Progress> report, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var pending = await PendingAsync(since, cancellationToken);
        var clock = Stopwatch.StartNew();
        int requests = 0, found = 0, notFound = 0, failures = 0, position = 0;
        TimeSpan? last = null;
        while (position < pending.Count && (maximumRequests is null || requests < maximumRequests))
        {
            var batch = pending.Skip(position).Take(BatchSize).ToList();
            var wait = last is { } previous ? previous + (interval ?? Interval) - clock.Elapsed : TimeSpan.Zero;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            last = clock.Elapsed;
            requests++;
            Answer? answer;
            try
            {
                using var response = await http.PostAsJsonAsync(Endpoint, batch.Select(cui => new { cui, data = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }), cancellationToken);
                if (response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
                {
                    throw new HttpRequestException($"ANAF answered {(int)response.StatusCode}.");
                }

                response.EnsureSuccessStatusCode();
                answer = await response.Content.ReadFromJsonAsync<Answer>(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
            {
                if (++failures >= MaximumConsecutiveFailures)
                {
                    throw new InvalidOperationException($"ANAF failed {failures} times in a row; the run stopped and can be resumed. Last error: {exception.Message}");
                }

                // Back off and retry the same batch.
                await Task.Delay((backoff ?? TimeSpan.FromSeconds(15)) * failures, cancellationToken);
                continue;
            }

            failures = 0;
            var rows = (answer?.Found ?? []).Select(item => Read(item, today)).ToList();
            var foundCuis = rows.Select(row => row.Cui).ToHashSet();
            rows.AddRange(batch.Where(cui => !foundCuis.Contains(cui)).Select(cui => new AnafStatus { Cui = cui, CheckedOn = today, Found = false }));
            await SaveAsync(rows, cancellationToken);
            found += foundCuis.Count;
            notFound += batch.Count - foundCuis.Count;
            position += batch.Count;
            if (requests % 100 == 0)
            {
                report(new Progress(requests, found, notFound, pending.Count - position, clock.Elapsed));
            }
        }

        return new Progress(requests, found, notFound, pending.Count - position, clock.Elapsed);
    }

    private async Task<List<long>> PendingAsync(DateOnly since, CancellationToken cancellationToken)
    {
        var latest = await database.FiscalReleases.MaxAsync(item => (DateOnly?)item.ReleaseDate, cancellationToken)
            ?? throw new InvalidOperationException("No MF identification release imported; run fiscal import-all first.");
        return await database.TaxpayerPeriods.AsNoTracking()
            .Where(item => item.Latest && item.ValidTo == latest)
            .Where(item => !database.AnafStatuses.Any(status => status.Cui == item.Cui && status.CheckedOn >= since))
            .OrderBy(item => item.Cui)
            .Select(item => item.Cui)
            .ToListAsync(cancellationToken);
    }

    private async Task SaveAsync(List<AnafStatus> rows, CancellationToken cancellationToken)
    {
        await database.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var batch = new NpgsqlBatch(connection);
        foreach (var row in rows)
        {
            var command = new NpgsqlBatchCommand("""
                INSERT INTO anaf.statuses (cui, checked_on, found, registration_status, tax_office, caen_code, vat_payer, vat_periods,
                    cash_accounting_vat, cash_accounting_vat_since, split_vat, inactive, inactive_since, reactivated_on, deregistered_on,
                    e_invoice, e_invoice_since)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8::jsonb, $9, $10, $11, $12, $13, $14, $15, $16, $17)
                ON CONFLICT (cui, checked_on) DO NOTHING
                """);
            object?[] values = [row.Cui, row.CheckedOn, row.Found, row.RegistrationStatus, row.TaxOffice, row.CaenCode, row.VatPayer, row.VatPeriods,
                row.CashAccountingVat, row.CashAccountingVatSince, row.SplitVat, row.Inactive, row.InactiveSince, row.ReactivatedOn, row.DeregisteredOn,
                row.EInvoice, row.EInvoiceSince];
            foreach (var value in values)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
            }

            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static AnafStatus Read(JsonElement item, DateOnly checkedOn)
    {
        var general = item.GetProperty("date_generale");
        var vat = Property(item, "inregistrare_scop_Tva");
        var cash = Property(item, "inregistrare_RTVAI");
        var inactive = Property(item, "stare_inactiv");
        var split = Property(item, "inregistrare_SplitTVA");
        return new AnafStatus
        {
            Cui = general.GetProperty("cui").GetInt64(),
            CheckedOn = checkedOn,
            Found = true,
            RegistrationStatus = Text(general, "stare_inregistrare"),
            TaxOffice = Text(general, "organFiscalCompetent"),
            CaenCode = Text(general, "cod_CAEN"),
            VatPayer = Flag(vat, "scpTVA"),
            VatPeriods = vat is { } periods && periods.TryGetProperty("perioade_TVA", out var list) ? list.GetRawText() : null,
            CashAccountingVat = Flag(cash, "statusTvaIncasare"),
            CashAccountingVatSince = Date(cash, "dataInceputTvaInc"),
            SplitVat = Flag(split, "statusSplitTVA"),
            Inactive = Flag(inactive, "statusInactivi"),
            InactiveSince = Date(inactive, "dataInactivare"),
            ReactivatedOn = Date(inactive, "dataReactivare"),
            DeregisteredOn = Date(inactive, "dataRadiere"),
            EInvoice = Flag(general, "statusRO_e_Factura"),
            EInvoiceSince = Date(general, "data_inreg_Reg_RO_e_Factura"),
        };

        static JsonElement? Property(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

        static string? Text(JsonElement? element, string name) =>
            element is { } value && value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { Length: > 0 } result ? result.Trim() : null;

        static bool? Flag(JsonElement? element, string name) =>
            element is { } value && value.TryGetProperty(name, out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean() : null;

        static DateOnly? Date(JsonElement? element, string name) =>
            DateOnly.TryParseExact(Text(element, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private sealed record Answer(int Cod, string? Message, List<JsonElement>? Found, List<long>? NotFound);
}
