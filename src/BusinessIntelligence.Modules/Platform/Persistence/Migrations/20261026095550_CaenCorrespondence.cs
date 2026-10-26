using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class CaenCorrespondence : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "class_code",
            schema: "financials",
            table: "statements",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "class_method",
            schema: "financials",
            table: "statements",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<short>(
            name: "class_version",
            schema: "financials",
            table: "statements",
            type: "smallint",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "caen_correspondence",
            schema: "registry",
            columns: table => new
            {
                rev2_code = table.Column<string>(type: "text", nullable: false),
                rev3_code = table.Column<string>(type: "text", nullable: false),
                intermediate = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_caen_correspondence", x => new { x.rev2_code, x.rev3_code });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "caen_correspondence",
            schema: "registry");

        migrationBuilder.DropColumn(
            name: "class_code",
            schema: "financials",
            table: "statements");

        migrationBuilder.DropColumn(
            name: "class_method",
            schema: "financials",
            table: "statements");

        migrationBuilder.DropColumn(
            name: "class_version",
            schema: "financials",
            table: "statements");
    }
}
