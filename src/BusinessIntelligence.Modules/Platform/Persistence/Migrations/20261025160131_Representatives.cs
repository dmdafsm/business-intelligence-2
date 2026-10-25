using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class Representatives : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "representatives",
            schema: "registry",
            columns: table => new
            {
                snapshot_id = table.Column<int>(type: "integer", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                row_number = table.Column<int>(type: "integer", nullable: false),
                registration_number = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: true),
                role = table.Column<string>(type: "text", nullable: true),
                birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                birth_date_text = table.Column<string>(type: "text", nullable: true),
                birth_locality = table.Column<string>(type: "text", nullable: true),
                birth_county = table.Column<string>(type: "text", nullable: true),
                birth_country = table.Column<string>(type: "text", nullable: true),
                locality = table.Column<string>(type: "text", nullable: true),
                county = table.Column<string>(type: "text", nullable: true),
                country = table.Column<string>(type: "text", nullable: true),
                person_kind = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_representatives", x => new { x.snapshot_id, x.source, x.row_number });
            });

        migrationBuilder.CreateIndex(
            name: "ix_representatives_snapshot_id_registration_number",
            schema: "registry",
            table: "representatives",
            columns: new[] { "snapshot_id", "registration_number" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "representatives",
            schema: "registry");
    }
}
