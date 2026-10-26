using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class FinancialBenchmarks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "benchmarks",
            schema: "financials",
            columns: table => new
            {
                year = table.Column<short>(type: "smallint", nullable: false),
                caen_version = table.Column<short>(type: "smallint", nullable: false),
                caen_code = table.Column<string>(type: "text", nullable: false),
                size_band = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                indicator = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                statements = table.Column<int>(type: "integer", nullable: false),
                population = table.Column<int>(type: "integer", nullable: false),
                p10 = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                p25 = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                p50 = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                p75 = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                p90 = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                formula_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_benchmarks", x => new { x.year, x.caen_version, x.caen_code, x.size_band, x.indicator });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "benchmarks",
            schema: "financials");
    }
}
