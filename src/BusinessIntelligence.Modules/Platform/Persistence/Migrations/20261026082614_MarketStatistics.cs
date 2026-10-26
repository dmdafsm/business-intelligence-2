using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class MarketStatistics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "market");

        migrationBuilder.CreateTable(
            name: "builds",
            schema: "market",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                registry_snapshot_id = table.Column<int>(type: "integer", nullable: false),
                reference_date = table.Column<DateOnly>(type: "date", nullable: false),
                built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_builds", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "lifecycle_changes",
            schema: "market",
            columns: table => new
            {
                observed_on = table.Column<DateOnly>(type: "date", nullable: false),
                county_number = table.Column<short>(type: "smallint", nullable: false),
                form_category = table.Column<string>(type: "text", nullable: false),
                from_lifecycle = table.Column<string>(type: "text", nullable: false),
                to_lifecycle = table.Column<string>(type: "text", nullable: false),
                previous_snapshot = table.Column<DateOnly>(type: "date", nullable: false),
                registrations = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_lifecycle_changes", x => new { x.observed_on, x.county_number, x.form_category, x.from_lifecycle, x.to_lifecycle });
            });

        migrationBuilder.CreateTable(
            name: "registrations",
            schema: "market",
            columns: table => new
            {
                month = table.Column<DateOnly>(type: "date", nullable: false),
                county_number = table.Column<short>(type: "smallint", nullable: false),
                form_category = table.Column<string>(type: "text", nullable: false),
                lifecycle = table.Column<string>(type: "text", nullable: false),
                companies = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_registrations", x => new { x.month, x.county_number, x.form_category, x.lifecycle });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "builds",
            schema: "market");

        migrationBuilder.DropTable(
            name: "lifecycle_changes",
            schema: "market");

        migrationBuilder.DropTable(
            name: "registrations",
            schema: "market");
    }
}
