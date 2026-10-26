using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class SearchFinancials : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "employees",
            schema: "search",
            table: "companies",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<short>(
            name: "financial_year",
            schema: "search",
            table: "companies",
            type: "smallint",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "net_result",
            schema: "search",
            table: "companies",
            type: "numeric(20,0)",
            precision: 20,
            scale: 0,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "turnover",
            schema: "search",
            table: "companies",
            type: "numeric(20,0)",
            precision: 20,
            scale: 0,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_companies_employees",
            schema: "search",
            table: "companies",
            column: "employees");

        migrationBuilder.CreateIndex(
            name: "ix_companies_turnover",
            schema: "search",
            table: "companies",
            column: "turnover");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_companies_employees",
            schema: "search",
            table: "companies");

        migrationBuilder.DropIndex(
            name: "ix_companies_turnover",
            schema: "search",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "employees",
            schema: "search",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "financial_year",
            schema: "search",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "net_result",
            schema: "search",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "turnover",
            schema: "search",
            table: "companies");
    }
}
