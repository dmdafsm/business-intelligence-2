using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "registry");

        migrationBuilder.CreateTable(
            name: "caen",
            schema: "registry",
            columns: table => new
            {
                version = table.Column<short>(type: "smallint", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                level = table.Column<string>(type: "text", nullable: false),
                section = table.Column<string>(type: "text", nullable: false),
                division = table.Column<string>(type: "text", nullable: true),
                group = table.Column<string>(type: "text", nullable: true),
                name = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_caen", x => new { x.version, x.code });
            });

        migrationBuilder.CreateTable(
            name: "companies",
            schema: "registry",
            columns: table => new
            {
                snapshot_id = table.Column<int>(type: "integer", nullable: false),
                company_key = table.Column<string>(type: "text", nullable: false),
                cui = table.Column<long>(type: "bigint", nullable: true),
                registration_number = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: true),
                legal_form = table.Column<string>(type: "text", nullable: true),
                form_category = table.Column<string>(type: "text", nullable: false),
                registration_date = table.Column<DateOnly>(type: "date", nullable: true),
                first_registration_date = table.Column<DateOnly>(type: "date", nullable: true),
                registration_count = table.Column<short>(type: "smallint", nullable: false),
                county_number = table.Column<short>(type: "smallint", nullable: true),
                uat_code = table.Column<int>(type: "integer", nullable: true),
                locality_code = table.Column<int>(type: "integer", nullable: true),
                geography_state = table.Column<string>(type: "text", nullable: false),
                website = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_companies", x => new { x.snapshot_id, x.company_key });
            });

        migrationBuilder.CreateTable(
            name: "registrations",
            schema: "registry",
            columns: table => new
            {
                snapshot_id = table.Column<int>(type: "integer", nullable: false),
                registration_number = table.Column<string>(type: "text", nullable: false),
                euid = table.Column<string>(type: "text", nullable: true),
                name = table.Column<string>(type: "text", nullable: true),
                cui = table.Column<long>(type: "bigint", nullable: true),
                cui_state = table.Column<string>(type: "text", nullable: false),
                legal_form = table.Column<string>(type: "text", nullable: true),
                form_category = table.Column<string>(type: "text", nullable: false),
                registration_date = table.Column<DateOnly>(type: "date", nullable: true),
                country = table.Column<string>(type: "text", nullable: true),
                county = table.Column<string>(type: "text", nullable: true),
                locality = table.Column<string>(type: "text", nullable: true),
                street = table.Column<string>(type: "text", nullable: true),
                street_number = table.Column<string>(type: "text", nullable: true),
                block = table.Column<string>(type: "text", nullable: true),
                staircase = table.Column<string>(type: "text", nullable: true),
                floor = table.Column<string>(type: "text", nullable: true),
                apartment = table.Column<string>(type: "text", nullable: true),
                postal_code = table.Column<string>(type: "text", nullable: true),
                sector = table.Column<string>(type: "text", nullable: true),
                address_details = table.Column<string>(type: "text", nullable: true),
                website = table.Column<string>(type: "text", nullable: true),
                parent_country = table.Column<string>(type: "text", nullable: true),
                county_number = table.Column<short>(type: "smallint", nullable: true),
                uat_code = table.Column<int>(type: "integer", nullable: true),
                locality_code = table.Column<int>(type: "integer", nullable: true),
                geography_state = table.Column<string>(type: "text", nullable: false),
                source_rows = table.Column<short>(type: "smallint", nullable: false),
                conflicting_fields = table.Column<string[]>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_registrations", x => new { x.snapshot_id, x.registration_number });
            });

        migrationBuilder.CreateTable(
            name: "siruta",
            schema: "registry",
            columns: table => new
            {
                code = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                postal_code = table.Column<string>(type: "text", nullable: true),
                county_number = table.Column<short>(type: "smallint", nullable: false),
                parent_code = table.Column<int>(type: "integer", nullable: false),
                type = table.Column<short>(type: "smallint", nullable: false),
                level = table.Column<short>(type: "smallint", nullable: false),
                environment = table.Column<short>(type: "smallint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_siruta", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "source_files",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                catalog_id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                dataset = table.Column<string>(type: "text", nullable: false),
                file_name = table.Column<string>(type: "text", nullable: false),
                url = table.Column<string>(type: "text", nullable: false),
                publisher = table.Column<string>(type: "text", nullable: false),
                license = table.Column<string>(type: "text", nullable: false),
                reference_date = table.Column<DateOnly>(type: "date", nullable: false),
                sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                bytes = table.Column<long>(type: "bigint", nullable: false),
                parser_version = table.Column<string>(type: "text", nullable: false),
                imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                report = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_source_files", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "status_codes",
            schema: "registry",
            columns: table => new
            {
                code = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_status_codes", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "published_datasets",
            schema: "registry",
            columns: table => new
            {
                kind = table.Column<string>(type: "text", nullable: false),
                source_file_id = table.Column<int>(type: "integer", nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_published_datasets", x => x.kind);
                table.ForeignKey(
                    name: "fk_published_datasets_source_files_source_file_id",
                    column: x => x.source_file_id,
                    principalSchema: "registry",
                    principalTable: "source_files",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_companies_snapshot_id_cui",
            schema: "registry",
            table: "companies",
            columns: new[] { "snapshot_id", "cui" });

        migrationBuilder.CreateIndex(
            name: "ix_companies_snapshot_id_registration_number",
            schema: "registry",
            table: "companies",
            columns: new[] { "snapshot_id", "registration_number" });

        migrationBuilder.CreateIndex(
            name: "ix_published_datasets_source_file_id",
            schema: "registry",
            table: "published_datasets",
            column: "source_file_id");

        migrationBuilder.CreateIndex(
            name: "ix_registrations_snapshot_id_cui",
            schema: "registry",
            table: "registrations",
            columns: new[] { "snapshot_id", "cui" });

        migrationBuilder.CreateIndex(
            name: "ix_siruta_parent_code",
            schema: "registry",
            table: "siruta",
            column: "parent_code");

        migrationBuilder.CreateIndex(
            name: "ix_source_files_sha256_parser_version",
            schema: "registry",
            table: "source_files",
            columns: new[] { "sha256", "parser_version" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "caen",
            schema: "registry");

        migrationBuilder.DropTable(
            name: "companies",
            schema: "registry");

        migrationBuilder.DropTable(
            name: "published_datasets",
            schema: "registry");

        migrationBuilder.DropTable(
            name: "registrations",
            schema: "registry");

        migrationBuilder.DropTable(
            name: "siruta",
            schema: "registry");

        migrationBuilder.DropTable(
            name: "status_codes",
            schema: "registry");

        migrationBuilder.DropTable(
            name: "source_files",
            schema: "registry");
    }
}
