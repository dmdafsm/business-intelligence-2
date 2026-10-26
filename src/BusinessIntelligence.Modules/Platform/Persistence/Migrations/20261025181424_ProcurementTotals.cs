using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class ProcurementTotals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_awards_winner_cui",
            schema: "procurement",
            table: "awards");

        migrationBuilder.AddColumn<DateOnly>(
            name: "awarded_on",
            schema: "procurement",
            table: "awards",
            type: "date",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "year_totals",
            schema: "procurement",
            columns: table => new
            {
                cui = table.Column<long>(type: "bigint", nullable: false),
                role = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                year = table.Column<short>(type: "smallint", nullable: false),
                contracts = table.Column<int>(type: "integer", nullable: false),
                contract_value = table.Column<decimal>(type: "numeric(22,2)", precision: 22, scale: 2, nullable: true),
                contracts_without_value = table.Column<int>(type: "integer", nullable: false),
                direct_purchases = table.Column<int>(type: "integer", nullable: false),
                direct_value = table.Column<decimal>(type: "numeric(22,2)", precision: 22, scale: 2, nullable: true),
                direct_without_value = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_year_totals", x => new { x.cui, x.role, x.year });
            });

        migrationBuilder.CreateIndex(
            name: "ix_awards_winner_cui_awarded_on",
            schema: "procurement",
            table: "awards",
            columns: new[] { "winner_cui", "awarded_on" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "year_totals",
            schema: "procurement");

        migrationBuilder.DropIndex(
            name: "ix_awards_winner_cui_awarded_on",
            schema: "procurement",
            table: "awards");

        migrationBuilder.DropColumn(
            name: "awarded_on",
            schema: "procurement",
            table: "awards");

        migrationBuilder.CreateIndex(
            name: "ix_awards_winner_cui",
            schema: "procurement",
            table: "awards",
            column: "winner_cui");
    }
}
