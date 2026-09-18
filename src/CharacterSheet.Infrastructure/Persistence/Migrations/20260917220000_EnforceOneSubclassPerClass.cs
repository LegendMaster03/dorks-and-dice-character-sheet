using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260917220000_EnforceOneSubclassPerClass")]
public partial class EnforceOneSubclassPerClass : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "UX_character_advancement_entries_SubclassPerClass",
            table: "character_advancement_entries",
            columns: new[] { "CharacterId", "ParentAdvancementEntryId" },
            unique: true,
            filter: "\"Kind\" = 'Subclass' AND \"ParentAdvancementEntryId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_character_advancement_entries_SubclassPerClass",
            table: "character_advancement_entries");
    }
}
