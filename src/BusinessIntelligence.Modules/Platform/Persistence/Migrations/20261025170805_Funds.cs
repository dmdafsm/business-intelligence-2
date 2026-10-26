using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class Funds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "funds");

        migrationBuilder.CreateTable(
            name: "projects",
            schema: "funds",
            columns: table => new
            {
                source_file_id = table.Column<int>(type: "integer", nullable: false),
                row_number = table.Column<int>(type: "integer", nullable: false),
                programme = table.Column<string>(type: "text", nullable: false),
                project_code = table.Column<string>(type: "text", nullable: true),
                title = table.Column<string>(type: "text", nullable: true),
                beneficiary_name = table.Column<string>(type: "text", nullable: true),
                beneficiary_name_key = table.Column<string>(type: "text", nullable: true, collation: "C"),
                beneficiary_cui = table.Column<long>(type: "bigint", nullable: true),
                county = table.Column<string>(type: "text", nullable: true),
                county_number = table.Column<short>(type: "smallint", nullable: true),
                locality = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "text", nullable: true),
                start_date = table.Column<DateOnly>(type: "date", nullable: true),
                end_date = table.Column<DateOnly>(type: "date", nullable: true),
                eligible_value = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                total_value = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                contracted_value = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                paid_value = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_projects", x => new { x.source_file_id, x.row_number });
            });

        migrationBuilder.CreateIndex(
            name: "ix_projects_beneficiary_cui",
            schema: "funds",
            table: "projects",
            column: "beneficiary_cui");

        migrationBuilder.CreateIndex(
            name: "ix_projects_beneficiary_name_key_county_number",
            schema: "funds",
            table: "projects",
            columns: new[] { "beneficiary_name_key", "county_number" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "projects",
            schema: "funds");
    }
}
