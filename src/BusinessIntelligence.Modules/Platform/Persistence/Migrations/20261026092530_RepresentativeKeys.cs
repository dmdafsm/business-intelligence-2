using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessIntelligence.Modules.Platform.Persistence.Migrations;

/// <inheritdoc />
public partial class RepresentativeKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "person_key",
            schema: "registry",
            table: "representatives",
            type: "text",
            nullable: true);

        // Rows imported before the key existed get it here; the same SQL as RepresentativeKeys.Sql at the time of writing.
        migrationBuilder.Sql("""
            UPDATE registry.representatives SET person_key =
            CASE WHEN regexp_replace(upper(translate(coalesce(name, ''), 'ăâîșşțţĂÂÎȘŞȚŢ', 'aaissttAAISSTT')), '[^A-Z0-9]', '', 'g') = '' THEN NULL
                 WHEN birth_date IS NOT NULL THEN
                     CASE WHEN regexp_replace(upper(translate(coalesce(birth_locality, ''), 'ăâîșşțţĂÂÎȘŞȚŢ', 'aaissttAAISSTT')), '[^A-Z0-9]', '', 'g') <> '' THEN 'N:' || regexp_replace(upper(translate(coalesce(name, ''), 'ăâîșşțţĂÂÎȘŞȚŢ', 'aaissttAAISSTT')), '[^A-Z0-9]', '', 'g') || ':' || birth_date::text || ':' || regexp_replace(upper(translate(coalesce(birth_locality, ''), 'ăâîșşțţĂÂÎȘŞȚŢ', 'aaissttAAISSTT')), '[^A-Z0-9]', '', 'g') END
                 WHEN birth_date_text IS NOT NULL THEN NULL
                 WHEN coalesce(locality, county, country) IS NOT NULL THEN 'L:' || regexp_replace(upper(translate(coalesce(name, ''), 'ăâîșşțţĂÂÎȘŞȚŢ', 'aaissttAAISSTT')), '[^A-Z0-9]', '', 'g')
            END
            """);

        migrationBuilder.CreateIndex(
            name: "ix_representatives_snapshot_id_person_key",
            schema: "registry",
            table: "representatives",
            columns: new[] { "snapshot_id", "person_key" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_representatives_snapshot_id_person_key",
            schema: "registry",
            table: "representatives");

        migrationBuilder.DropColumn(
            name: "person_key",
            schema: "registry",
            table: "representatives");
    }
}
