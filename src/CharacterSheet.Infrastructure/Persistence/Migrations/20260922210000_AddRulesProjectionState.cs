using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260922210000_AddRulesProjectionState")]
public partial class AddRulesProjectionState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_hit_point_gains",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                AdvancementOccurrenceId = table.Column<Guid>(type: "uuid", nullable: false),
                ClassLevel = table.Column<int>(type: "integer", nullable: false),
                HitDieValue = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_hit_point_gains", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_hit_point_gains_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "character_rules_inputs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                IntegerValue = table.Column<int>(type: "integer", nullable: true),
                BooleanValue = table.Column<bool>(type: "boolean", nullable: true),
                TextValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_rules_inputs", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_rules_inputs_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_character_hit_point_gains_CharacterId_AdvancementOccurrenceId_ClassLevel",
            table: "character_hit_point_gains",
            columns: new[] { "CharacterId", "AdvancementOccurrenceId", "ClassLevel" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_character_rules_inputs_CharacterId_Kind_Key",
            table: "character_rules_inputs",
            columns: new[] { "CharacterId", "Kind", "Key" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_hit_point_gains");
        migrationBuilder.DropTable(name: "character_rules_inputs");
    }
}
