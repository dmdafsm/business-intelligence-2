using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class Procurement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "procurement");

        migrationBuilder.CreateTable(
            name: "awards",
            schema: "procurement",
            columns: table => new
            {
                source_file_id = table.Column<int>(type: "integer", nullable: false),
                row_number = table.Column<int>(type: "integer", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                year = table.Column<short>(type: "smallint", nullable: false),
                quarter = table.Column<short>(type: "smallint", nullable: false),
                authority_name = table.Column<string>(type: "text", nullable: true),
                authority_cui = table.Column<long>(type: "bigint", nullable: true),
                reference = table.Column<string>(type: "text", nullable: true),
                procedure = table.Column<string>(type: "text", nullable: true),
                contract_type = table.Column<string>(type: "text", nullable: true),
                cpv_code = table.Column<string>(type: "text", nullable: true),
                cpv_name = table.Column<string>(type: "text", nullable: true),
                title = table.Column<string>(type: "text", nullable: true),
                lot = table.Column<string>(type: "text", nullable: true),
                contract_number = table.Column<string>(type: "text", nullable: true),
                published_on = table.Column<DateOnly>(type: "date", nullable: true),
                contract_date = table.Column<DateOnly>(type: "date", nullable: true),
                value_ron = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                eu_funded = table.Column<bool>(type: "boolean", nullable: true),
                programme = table.Column<string>(type: "text", nullable: true),
                winner_name = table.Column<string>(type: "text", nullable: true),
                winner_cui = table.Column<long>(type: "bigint", nullable: true),
                winner_cui_state = table.Column<string>(type: "text", nullable: false),
                winners = table.Column<short>(type: "smallint", nullable: false),
                first_winner = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_awards", x => new { x.source_file_id, x.row_number });
            });

        migrationBuilder.CreateIndex(
            name: "ix_awards_authority_cui",
            schema: "procurement",
            table: "awards",
            column: "authority_cui");

        migrationBuilder.CreateIndex(
            name: "ix_awards_winner_cui",
            schema: "procurement",
            table: "awards",
            column: "winner_cui");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "awards",
            schema: "procurement");
    }
}
