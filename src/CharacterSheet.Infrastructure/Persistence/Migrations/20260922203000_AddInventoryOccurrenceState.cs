using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260922203000_AddInventoryOccurrenceState")]
public partial class AddInventoryOccurrenceState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ContainerOccurrenceId",
            table: "character_inventory_item_occurrences",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsAttuned",
            table: "character_inventory_item_occurrences",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsCarried",
            table: "character_inventory_item_occurrences",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsEquipped",
            table: "character_inventory_item_occurrences",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "Quantity",
            table: "character_inventory_item_occurrences",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAt",
            table: "character_inventory_item_occurrences",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "character_inventory_item_occurrences"
            SET "UpdatedAt" = "CreatedAt"
            WHERE "UpdatedAt" IS NULL;
            """);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "UpdatedAt",
            table: "character_inventory_item_occurrences",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_character_inventory_item_occurrences_CharacterId_ContainerOccurrenceId",
            table: "character_inventory_item_occurrences",
            columns: new[] { "CharacterId", "ContainerOccurrenceId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_character_inventory_item_occurrences_CharacterId_ContainerOccurrenceId",
            table: "character_inventory_item_occurrences");

        migrationBuilder.DropColumn(
            name: "ContainerOccurrenceId",
            table: "character_inventory_item_occurrences");
        migrationBuilder.DropColumn(
            name: "IsAttuned",
            table: "character_inventory_item_occurrences");
        migrationBuilder.DropColumn(
            name: "IsCarried",
            table: "character_inventory_item_occurrences");
        migrationBuilder.DropColumn(
            name: "IsEquipped",
            table: "character_inventory_item_occurrences");
        migrationBuilder.DropColumn(
            name: "Quantity",
            table: "character_inventory_item_occurrences");
        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "character_inventory_item_occurrences");
    }
}
