using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class SearchSignals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string[]>(
            name: "signals",
            schema: "search",
            table: "companies",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'::text[]");

        migrationBuilder.CreateIndex(
            name: "ix_companies_signals",
            schema: "search",
            table: "companies",
            column: "signals")
            .Annotation("Npgsql:IndexMethod", "gin");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_companies_signals",
            schema: "search",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "signals",
            schema: "search",
            table: "companies");
    }
}
