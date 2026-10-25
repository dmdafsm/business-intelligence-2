using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class Financials : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "financials");

        migrationBuilder.CreateTable(
            name: "statements",
            schema: "financials",
            columns: table => new
            {
                year = table.Column<short>(type: "smallint", nullable: false),
                cui = table.Column<long>(type: "bigint", nullable: false),
                family = table.Column<string>(type: "text", nullable: false),
                caen_code = table.Column<string>(type: "text", nullable: true),
                caen_version = table.Column<short>(type: "smallint", nullable: true),
                source_file_id = table.Column<int>(type: "integer", nullable: false),
                fixed_assets = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                current_assets = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                inventories = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                receivables = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                cash = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                prepaid_expenses = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                liabilities = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                deferred_income = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                provisions = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                equity = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                share_capital = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                regie_patrimony = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                public_patrimony = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                turnover = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                total_income = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                total_expenses = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                gross_profit = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                gross_loss = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                net_profit = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                net_loss = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                employees = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_statements", x => new { x.cui, x.year });
            });

        migrationBuilder.CreateIndex(
            name: "ix_statements_year_family",
            schema: "financials",
            table: "statements",
            columns: new[] { "year", "family" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "statements",
            schema: "financials");
    }
}
