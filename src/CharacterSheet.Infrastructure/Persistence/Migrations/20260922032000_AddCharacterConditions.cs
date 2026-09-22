using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260922032000_AddCharacterConditions")]
public partial class AddCharacterConditions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_conditions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                RuleConceptKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                CustomName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                Level = table.Column<int>(type: "integer", nullable: true),
                CounterCurrent = table.Column<int>(type: "integer", nullable: true),
                CounterMaximum = table.Column<int>(type: "integer", nullable: true),
                Duration = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_conditions", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_conditions_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_character_conditions_CharacterId",
            table: "character_conditions",
            column: "CharacterId");

        migrationBuilder.CreateIndex(
            name: "IX_character_conditions_CharacterId_RuleConceptKey",
            table: "character_conditions",
            columns: new[] { "CharacterId", "RuleConceptKey" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_conditions");
    }
}
