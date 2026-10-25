using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class FiscalHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "fiscal");

        migrationBuilder.CreateTable(
            name: "releases",
            schema: "fiscal",
            columns: table => new
            {
                release_date = table.Column<DateOnly>(type: "date", nullable: false),
                imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                parser_version = table.Column<string>(type: "text", nullable: false),
                report = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_releases", x => x.release_date);
            });

        migrationBuilder.CreateTable(
            name: "taxpayer_kinds",
            schema: "fiscal",
            columns: table => new
            {
                cui = table.Column<long>(type: "bigint", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                release_date = table.Column<DateOnly>(type: "date", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_taxpayer_kinds", x => x.cui);
            });

        migrationBuilder.CreateTable(
            name: "taxpayer_periods",
            schema: "fiscal",
            columns: table => new
            {
                cui = table.Column<long>(type: "bigint", nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                latest = table.Column<bool>(type: "boolean", nullable: false),
                releases = table.Column<short>(type: "smallint", nullable: false),
                name = table.Column<string>(type: "text", nullable: true),
                name_key = table.Column<string>(type: "text", nullable: true),
                kind = table.Column<string>(type: "text", nullable: true),
                unit_type = table.Column<string>(type: "text", nullable: true),
                parent_cui = table.Column<long>(type: "bigint", nullable: true),
                county = table.Column<string>(type: "text", nullable: true),
                locality = table.Column<string>(type: "text", nullable: true),
                sector = table.Column<string>(type: "text", nullable: true),
                place_key = table.Column<string>(type: "text", nullable: true),
                street = table.Column<string>(type: "text", nullable: true),
                street_number = table.Column<string>(type: "text", nullable: true),
                street_key = table.Column<string>(type: "text", nullable: true),
                block = table.Column<string>(type: "text", nullable: true),
                staircase = table.Column<string>(type: "text", nullable: true),
                floor = table.Column<string>(type: "text", nullable: true),
                apartment = table.Column<string>(type: "text", nullable: true),
                address_details = table.Column<string>(type: "text", nullable: true),
                postal_code = table.Column<string>(type: "text", nullable: true),
                phone = table.Column<string>(type: "text", nullable: true),
                fax = table.Column<string>(type: "text", nullable: true),
                registration_number = table.Column<string>(type: "text", nullable: true),
                authorization_act = table.Column<string>(type: "text", nullable: true),
                vat_payer = table.Column<bool>(type: "boolean", nullable: true),
                vat_since = table.Column<DateOnly>(type: "date", nullable: true),
                fiscal_status = table.Column<string>(type: "text", nullable: true),
                status_date = table.Column<DateOnly>(type: "date", nullable: true),
                deregistration_date = table.Column<DateOnly>(type: "date", nullable: true),
                last_declaration = table.Column<DateOnly>(type: "date", nullable: true),
                last_processed = table.Column<DateOnly>(type: "date", nullable: true),
                obligations = table.Column<int[]>(type: "integer[]", nullable: false),
                obligation_dates = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_taxpayer_periods", x => new { x.cui, x.valid_from });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "releases",
            schema: "fiscal");

        migrationBuilder.DropTable(
            name: "taxpayer_kinds",
            schema: "fiscal");

        migrationBuilder.DropTable(
            name: "taxpayer_periods",
            schema: "fiscal");
    }
}
