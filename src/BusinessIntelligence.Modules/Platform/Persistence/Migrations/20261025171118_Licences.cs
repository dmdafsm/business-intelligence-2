using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class Licences : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "licences");

        migrationBuilder.CreateTable(
            name: "licences",
            schema: "licences",
            columns: table => new
            {
                source_file_id = table.Column<int>(type: "integer", nullable: false),
                row_number = table.Column<int>(type: "integer", nullable: false),
                authority = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                holder_name = table.Column<string>(type: "text", nullable: true),
                holder_name_key = table.Column<string>(type: "text", nullable: true, collation: "C"),
                holder_cui = table.Column<long>(type: "bigint", nullable: true),
                activity = table.Column<string>(type: "text", nullable: true),
                details = table.Column<string>(type: "text", nullable: true),
                place = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_licences", x => new { x.source_file_id, x.row_number });
            });

        migrationBuilder.CreateIndex(
            name: "ix_licences_holder_cui",
            schema: "licences",
            table: "licences",
            column: "holder_cui");

        migrationBuilder.CreateIndex(
            name: "ix_licences_holder_name_key",
            schema: "licences",
            table: "licences",
            column: "holder_name_key");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "licences",
            schema: "licences");
    }
}
