using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class AnafStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "anaf");

        migrationBuilder.CreateTable(
            name: "statuses",
            schema: "anaf",
            columns: table => new
            {
                cui = table.Column<long>(type: "bigint", nullable: false),
                checked_on = table.Column<DateOnly>(type: "date", nullable: false),
                found = table.Column<bool>(type: "boolean", nullable: false),
                registration_status = table.Column<string>(type: "text", nullable: true),
                tax_office = table.Column<string>(type: "text", nullable: true),
                caen_code = table.Column<string>(type: "text", nullable: true),
                vat_payer = table.Column<bool>(type: "boolean", nullable: true),
                vat_periods = table.Column<string>(type: "jsonb", nullable: true),
                cash_accounting_vat = table.Column<bool>(type: "boolean", nullable: true),
                cash_accounting_vat_since = table.Column<DateOnly>(type: "date", nullable: true),
                split_vat = table.Column<bool>(type: "boolean", nullable: true),
                inactive = table.Column<bool>(type: "boolean", nullable: true),
                inactive_since = table.Column<DateOnly>(type: "date", nullable: true),
                reactivated_on = table.Column<DateOnly>(type: "date", nullable: true),
                deregistered_on = table.Column<DateOnly>(type: "date", nullable: true),
                e_invoice = table.Column<bool>(type: "boolean", nullable: true),
                e_invoice_since = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_statuses", x => new { x.cui, x.checked_on });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "statuses",
            schema: "anaf");
    }
}
