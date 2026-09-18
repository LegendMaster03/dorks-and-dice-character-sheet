using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260918070000_AddCharacterRoutineState")]
public partial class AddCharacterRoutineState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_inventory_item_occurrences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                RuleConceptKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_inventory_item_occurrences", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_inventory_item_occurrences_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "character_notes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_notes", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_notes_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_character_inventory_item_occurrences_CharacterId_RuleConceptKey",
            table: "character_inventory_item_occurrences",
            columns: new[] { "CharacterId", "RuleConceptKey" });

        migrationBuilder.CreateIndex(
            name: "IX_character_notes_CharacterId",
            table: "character_notes",
            column: "CharacterId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_inventory_item_occurrences");
        migrationBuilder.DropTable(name: "character_notes");
    }
}
