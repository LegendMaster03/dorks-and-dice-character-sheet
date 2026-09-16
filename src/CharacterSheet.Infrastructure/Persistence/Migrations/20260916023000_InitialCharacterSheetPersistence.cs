using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

public partial class InitialCharacterSheetPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_sheet_roots",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                BuilderStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_sheet_roots", x => x.CharacterId);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_sheet_roots");
    }
}
