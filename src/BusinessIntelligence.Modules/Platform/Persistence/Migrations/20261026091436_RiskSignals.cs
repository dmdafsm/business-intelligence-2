using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class RiskSignals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "risk");

        migrationBuilder.CreateTable(
            name: "builds",
            schema: "risk",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                financial_year = table.Column<short>(type: "smallint", nullable: true),
                benchmark_year = table.Column<short>(type: "smallint", nullable: true),
                version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_builds", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "signals",
            schema: "risk",
            columns: table => new
            {
                company_key = table.Column<string>(type: "text", nullable: false),
                signal = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                cui = table.Column<long>(type: "bigint", nullable: true),
                year = table.Column<short>(type: "smallint", nullable: true),
                amount = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                ratio = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                threshold = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                caen_code = table.Column<string>(type: "text", nullable: true),
                since = table.Column<DateOnly>(type: "date", nullable: true),
                checked_on = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_signals", x => new { x.company_key, x.signal });
            });

        migrationBuilder.CreateIndex(
            name: "ix_signals_cui",
            schema: "risk",
            table: "signals",
            column: "cui");

        migrationBuilder.CreateIndex(
            name: "ix_signals_signal_company_key",
            schema: "risk",
            table: "signals",
            columns: new[] { "signal", "company_key" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "builds",
            schema: "risk");

        migrationBuilder.DropTable(
            name: "signals",
            schema: "risk");
    }
}
