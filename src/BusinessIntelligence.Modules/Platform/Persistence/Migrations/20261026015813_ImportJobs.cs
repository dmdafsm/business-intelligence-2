using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class ImportJobs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "jobs");

        migrationBuilder.CreateTable(
            name: "catalog_files",
            schema: "jobs",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                dataset = table.Column<string>(type: "text", nullable: false),
                file_name = table.Column<string>(type: "text", nullable: false),
                url = table.Column<string>(type: "text", nullable: false),
                publisher = table.Column<string>(type: "text", nullable: false),
                license = table.Column<string>(type: "text", nullable: false),
                reference_date = table.Column<DateOnly>(type: "date", nullable: false),
                bytes = table.Column<long>(type: "bigint", nullable: false),
                sha256 = table.Column<string>(type: "text", nullable: false),
                year = table.Column<short>(type: "smallint", nullable: true),
                family = table.Column<string>(type: "text", nullable: true),
                local_path = table.Column<string>(type: "text", nullable: false),
                grouping = table.Column<string>(type: "text", nullable: false),
                job_id = table.Column<string>(type: "text", nullable: false),
                run_id = table.Column<long>(type: "bigint", nullable: true),
                resource_id = table.Column<string>(type: "text", nullable: false),
                resource_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_catalog_files", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "runs",
            schema: "jobs",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                job_id = table.Column<string>(type: "text", nullable: false),
                trigger = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                force = table.Column<bool>(type: "boolean", nullable: false),
                cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                step = table.Column<string>(type: "text", nullable: true),
                new_files = table.Column<int>(type: "integer", nullable: false),
                error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_runs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "run_lines",
            schema: "jobs",
            columns: table => new
            {
                run_id = table.Column<long>(type: "bigint", nullable: false),
                number = table.Column<int>(type: "integer", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                stream = table.Column<string>(type: "text", nullable: false),
                text = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_run_lines", x => new { x.run_id, x.number });
                table.ForeignKey(
                    name: "fk_run_lines_runs_run_id",
                    column: x => x.run_id,
                    principalSchema: "jobs",
                    principalTable: "runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_catalog_files_kind_reference_date",
            schema: "jobs",
            table: "catalog_files",
            columns: new[] { "kind", "reference_date" });

        migrationBuilder.CreateIndex(
            name: "ix_catalog_files_local_path",
            schema: "jobs",
            table: "catalog_files",
            column: "local_path",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_catalog_files_resource_id",
            schema: "jobs",
            table: "catalog_files",
            column: "resource_id");

        migrationBuilder.CreateIndex(
            name: "ix_runs_active_job",
            schema: "jobs",
            table: "runs",
            column: "job_id",
            unique: true,
            filter: "status IN ('queued', 'running')");

        migrationBuilder.CreateIndex(
            name: "ix_runs_job_id_queued_at",
            schema: "jobs",
            table: "runs",
            columns: new[] { "job_id", "queued_at" });

        migrationBuilder.CreateIndex(
            name: "ix_runs_status",
            schema: "jobs",
            table: "runs",
            column: "status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "catalog_files",
            schema: "jobs");

        migrationBuilder.DropTable(
            name: "run_lines",
            schema: "jobs");

        migrationBuilder.DropTable(
            name: "runs",
            schema: "jobs");
    }
}
