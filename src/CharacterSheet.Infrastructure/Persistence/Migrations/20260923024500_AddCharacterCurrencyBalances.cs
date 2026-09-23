using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260923024500_AddCharacterCurrencyBalances")]
public partial class AddCharacterCurrencyBalances : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_currency_balances",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                CurrencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Amount = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_currency_balances", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_currency_balances_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_character_currency_balances_CharacterId_CurrencyKey",
            table: "character_currency_balances",
            columns: new[] { "CharacterId", "CurrencyKey" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_currency_balances");
    }
}
