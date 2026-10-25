using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class SearchIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "search");

        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

        migrationBuilder.CreateTable(
            name: "builds",
            schema: "search",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                registry_snapshot_id = table.Column<int>(type: "integer", nullable: false),
                built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                report = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_builds", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "companies",
            schema: "search",
            columns: table => new
            {
                company_key = table.Column<string>(type: "text", nullable: false),
                cui = table.Column<long>(type: "bigint", nullable: true),
                registration_number = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: true),
                name_key = table.Column<string>(type: "text", nullable: true, collation: "C"),
                legal_form = table.Column<string>(type: "text", nullable: true),
                lifecycle = table.Column<string>(type: "text", nullable: false),
                first_registration_date = table.Column<DateOnly>(type: "date", nullable: true),
                county_number = table.Column<short>(type: "smallint", nullable: true),
                uat_code = table.Column<int>(type: "integer", nullable: true),
                locality_code = table.Column<int>(type: "integer", nullable: true),
                caen_version = table.Column<short>(type: "smallint", nullable: true),
                caen = table.Column<string[]>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_companies", x => x.company_key);
            });

        migrationBuilder.CreateTable(
            name: "counts",
            schema: "search",
            columns: table => new
            {
                county_number = table.Column<short>(type: "smallint", nullable: false),
                lifecycle = table.Column<string>(type: "text", nullable: false),
                caen = table.Column<string>(type: "text", nullable: false),
                companies = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_counts", x => new { x.county_number, x.lifecycle, x.caen });
            });

        migrationBuilder.CreateTable(
            name: "names",
            schema: "search",
            columns: table => new
            {
                company_key = table.Column<string>(type: "text", nullable: false),
                name_key = table.Column<string>(type: "text", nullable: false, collation: "C"),
                name = table.Column<string>(type: "text", nullable: false),
                current = table.Column<bool>(type: "boolean", nullable: false),
                last_seen = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_names", x => new { x.company_key, x.name_key });
            });

        migrationBuilder.CreateTable(
            name: "registration_numbers",
            schema: "search",
            columns: table => new
            {
                number_key = table.Column<string>(type: "text", nullable: false),
                company_key = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_registration_numbers", x => new { x.number_key, x.company_key });
            });

        migrationBuilder.CreateIndex(
            name: "ix_companies_caen",
            schema: "search",
            table: "companies",
            column: "caen")
            .Annotation("Npgsql:IndexMethod", "gin");

        migrationBuilder.CreateIndex(
            name: "ix_companies_county_number_uat_code",
            schema: "search",
            table: "companies",
            columns: new[] { "county_number", "uat_code" });

        migrationBuilder.CreateIndex(
            name: "ix_companies_cui",
            schema: "search",
            table: "companies",
            column: "cui");

        migrationBuilder.CreateIndex(
            name: "ix_companies_name_key",
            schema: "search",
            table: "companies",
            column: "name_key");

        migrationBuilder.CreateIndex(
            name: "ix_names_name_key_prefix",
            schema: "search",
            table: "names",
            column: "name_key");

        migrationBuilder.CreateIndex(
            name: "ix_names_name_key_trgm",
            schema: "search",
            table: "names",
            column: "name_key")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "builds",
            schema: "search");

        migrationBuilder.DropTable(
            name: "companies",
            schema: "search");

        migrationBuilder.DropTable(
            name: "counts",
            schema: "search");

        migrationBuilder.DropTable(
            name: "names",
            schema: "search");

        migrationBuilder.DropTable(
            name: "registration_numbers",
            schema: "search");

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
    }
}
