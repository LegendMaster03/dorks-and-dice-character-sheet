using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261003022500_AddStartingEquipmentCurrencyGrants")]
public partial class AddStartingEquipmentCurrencyGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_starting_equipment_currency_grants",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceGrantKey = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                CurrencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                AppliedAmount = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_character_starting_equipment_currency_grants",
                    value => new { value.CharacterId, value.SourceGrantKey });
                table.ForeignKey(
                    name: "FK_character_starting_equipment_currency_grants_character_sheet_roots_CharacterId",
                    column: value => value.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "character_starting_equipment_currency_grants");
    }
}