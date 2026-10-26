using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class SearchCaenUpper : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string[]>(
            name: "caen_upper",
            schema: "search",
            table: "companies",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'::text[]");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "caen_upper",
            schema: "search",
            table: "companies");
    }
}
