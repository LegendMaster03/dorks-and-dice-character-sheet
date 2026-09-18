using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260918034000_AddBaseAbilityScoreInputs")]
public partial class AddBaseAbilityScoreInputs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_base_ability_score_inputs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                AbilityKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Score = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_base_ability_score_inputs", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_base_ability_score_inputs_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_character_base_ability_score_inputs_CharacterId_AbilityKey",
            table: "character_base_ability_score_inputs",
            columns: new[] { "CharacterId", "AbilityKey" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_base_ability_score_inputs");
    }
}
