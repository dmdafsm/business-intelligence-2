using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class RegistryStatusesAndActivities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "lifecycle",
            schema: "registry",
            table: "registrations",
            type: "text",
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.AddColumn<short>(
            name: "caen_version",
            schema: "registry",
            table: "companies",
            type: "smallint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "lifecycle",
            schema: "registry",
            table: "companies",
            type: "text",
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.CreateTable(
            name: "company_activities",
            schema: "registry",
            columns: table => new
            {
                snapshot_id = table.Column<int>(type: "integer", nullable: false),
                company_key = table.Column<string>(type: "text", nullable: false),
                caen_version = table.Column<short>(type: "smallint", nullable: false),
                caen_code = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_company_activities", x => new { x.snapshot_id, x.company_key, x.caen_version, x.caen_code });
            });

        migrationBuilder.CreateTable(
            name: "registration_statuses",
            schema: "registry",
            columns: table => new
            {
                snapshot_id = table.Column<int>(type: "integer", nullable: false),
                registration_number = table.Column<string>(type: "text", nullable: false),
                status_code = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_registration_statuses", x => new { x.snapshot_id, x.registration_number, x.status_code });
            });

        migrationBuilder.CreateIndex(
            name: "ix_company_activities_snapshot_id_caen_version_caen_code",
            schema: "registry",
            table: "company_activities",
            columns: new[] { "snapshot_id", "caen_version", "caen_code" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "company_activities",
            schema: "registry");

        migrationBuilder.DropTable(
            name: "registration_statuses",
            schema: "registry");

        migrationBuilder.DropColumn(
            name: "lifecycle",
            schema: "registry",
            table: "registrations");

        migrationBuilder.DropColumn(
            name: "caen_version",
            schema: "registry",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "lifecycle",
            schema: "registry",
            table: "companies");
    }
}
