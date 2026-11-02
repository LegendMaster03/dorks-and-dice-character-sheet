using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// The old starting-equipment grant mappings were an ongoing synchronization
/// layer. Acquired equipment now lives only in ordinary Character inventory;
/// a single server-owned receipt prevents repeated initial awards.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261101190000_RetireStartingEquipmentSynchronization")]
public partial class RetireStartingEquipmentSynchronization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // This removes only obsolete grant-provenance rows. It does not remove
        // any Character inventory occurrences, containers, or currency balances.
        migrationBuilder.DropTable(
            name: "character_starting_equipment_inventory_grants");
        migrationBuilder.DropTable(
            name: "character_starting_equipment_currency_grants");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_starting_equipment_inventory_grants",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                InventoryOccurrenceId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceGrantKey = table.Column<string>(
                    type: "character varying(800)", maxLength: 800, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_character_starting_equipment_inventory_grants",
                    value => value.InventoryOccurrenceId);
                table.ForeignKey(
                    name: "FK_character_starting_equipment_inventory_grants_character_inventory_item_occurrences_InventoryOccurrenceId",
                    column: value => value.InventoryOccurrenceId,
                    principalTable: "character_inventory_item_occurrences",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_character_starting_equipment_inventory_grants_character_sheet_roots_CharacterId",
                    column: value => value.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_character_starting_equipment_inventory_grants_CharacterId_SourceGrantKey",
            table: "character_starting_equipment_inventory_grants",
            columns: new[] { "CharacterId", "SourceGrantKey" },
            unique: true);

        migrationBuilder.CreateTable(
            name: "character_starting_equipment_currency_grants",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceGrantKey = table.Column<string>(
                    type: "character varying(800)", maxLength: 800, nullable: false),
                CurrencyKey = table.Column<string>(
                    type: "character varying(160)", maxLength: 160, nullable: false),
                AppliedAmount = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false)
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
}
