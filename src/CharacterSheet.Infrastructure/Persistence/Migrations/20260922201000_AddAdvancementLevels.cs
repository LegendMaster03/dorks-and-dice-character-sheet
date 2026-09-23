using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260922201000_AddAdvancementLevels")]
public partial class AddAdvancementLevels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Level",
            table: "character_advancement_entries",
            type: "integer",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "character_advancement_entries"
            SET "Level" = 1
            WHERE "Kind" IN ('Class', 'PrestigeClass') AND "Level" IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Level",
            table: "character_advancement_entries");
    }
}
