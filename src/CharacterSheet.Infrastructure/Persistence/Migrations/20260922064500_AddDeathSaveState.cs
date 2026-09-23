using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260922064500_AddDeathSaveState")]
public partial class AddDeathSaveState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DeathSaveFailures",
            table: "character_sheet_roots",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "DeathSaveSuccesses",
            table: "character_sheet_roots",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DeathSaveFailures",
            table: "character_sheet_roots");

        migrationBuilder.DropColumn(
            name: "DeathSaveSuccesses",
            table: "character_sheet_roots");
    }
}
