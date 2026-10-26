using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class AreaOutlines : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "area_outlines",
            schema: "registry",
            columns: table => new
            {
                code = table.Column<int>(type: "integer", nullable: false),
                level = table.Column<short>(type: "smallint", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                path = table.Column<string>(type: "text", nullable: false),
                source_file_id = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_area_outlines", x => new { x.level, x.code });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "area_outlines",
            schema: "registry");
    }
}
