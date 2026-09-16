using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260916060000_AddCharacterBuildSelections")]
public partial class AddCharacterBuildSelections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_advancement_entries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RuleConceptKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Ordinal = table.Column<int>(type: "INTEGER", nullable: true),
                ParentAdvancementEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_advancement_entries", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_advancement_entries_character_advancement_entries_ParentAdvancementEntryId",
                    column: x => x.ParentAdvancementEntryId,
                    principalTable: "character_advancement_entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_character_advancement_entries_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "character_foundational_rule_selections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RuleConceptKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_foundational_rule_selections", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_foundational_rule_selections_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_character_advancement_entries_CharacterId_Ordinal",
            table: "character_advancement_entries",
            columns: new[] { "CharacterId", "Ordinal" });

        migrationBuilder.CreateIndex(
            name: "IX_character_advancement_entries_ParentAdvancementEntryId",
            table: "character_advancement_entries",
            column: "ParentAdvancementEntryId");

        migrationBuilder.CreateIndex(
            name: "IX_character_foundational_rule_selections_CharacterId_Category",
            table: "character_foundational_rule_selections",
            columns: new[] { "CharacterId", "Category" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_advancement_entries");
        migrationBuilder.DropTable(name: "character_foundational_rule_selections");
    }
}
