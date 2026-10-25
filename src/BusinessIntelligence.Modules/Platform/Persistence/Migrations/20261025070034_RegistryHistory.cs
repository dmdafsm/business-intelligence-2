using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class RegistryHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "history_snapshots",
            schema: "registry",
            columns: table => new
            {
                snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                parser_version = table.Column<string>(type: "text", nullable: false),
                report = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_history_snapshots", x => x.snapshot_date);
            });

        migrationBuilder.CreateTable(
            name: "registration_periods",
            schema: "registry",
            columns: table => new
            {
                registration_number = table.Column<string>(type: "text", nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                name = table.Column<string>(type: "text", nullable: true),
                name_key = table.Column<string>(type: "text", nullable: true),
                cui = table.Column<long>(type: "bigint", nullable: true),
                cui_state = table.Column<string>(type: "text", nullable: false),
                county = table.Column<string>(type: "text", nullable: true),
                locality = table.Column<string>(type: "text", nullable: true),
                address = table.Column<string>(type: "text", nullable: true),
                county_number = table.Column<short>(type: "smallint", nullable: true),
                uat_code = table.Column<int>(type: "integer", nullable: true),
                locality_code = table.Column<int>(type: "integer", nullable: true),
                lifecycle = table.Column<string>(type: "text", nullable: false),
                status_codes = table.Column<int[]>(type: "integer[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_registration_periods", x => new { x.registration_number, x.valid_from });
            });

        migrationBuilder.CreateIndex(
            name: "ix_registration_periods_cui",
            schema: "registry",
            table: "registration_periods",
            column: "cui");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "history_snapshots",
            schema: "registry");

        migrationBuilder.DropTable(
            name: "registration_periods",
            schema: "registry");
    }
}
